using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.Notifications;

/// <summary>
/// DTO for a single overdue notification produced by the computation.
/// </summary>
public sealed record OverdueNotificationDto
{
    public required string Id { get; init; }
    /// <summary>"piutang" or "utang"</summary>
    public required string Type { get; init; }
    /// <summary>"success" (31d, hijau), "warning" (61d, kuning), "danger" (91d, merah)</summary>
    public required string Severity { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string InvoiceNumber { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string EntityName { get; init; }
    public string? CounterpartyName { get; init; }
    public string? DueDate { get; init; }
    public required int DaysPastDue { get; init; }
    /// <summary>The threshold that was crossed: 31, 61, or 91.</summary>
    public required int AgingBucket { get; init; }
    public required DateTime CreatedAt { get; init; }
    /// <summary>"alert" for overdue, "system" for future use.</summary>
    public string Category { get; init; } = "alert";
}

/// <summary>
/// Computes per-invoice overdue notifications from Accurate sales/purchase invoice data.
/// Produces notifications for invoices that have recently crossed the 31, 61, or 91 day thresholds.
/// </summary>
public static class OverdueNotificationComputation
{
    /// <summary>Max invoices to fetch detail for, per company.</summary>
    private const int MaxInvoiceDetails = 500;
    private const int MaxParallelism = 6;

    /// <summary>
    /// Days window: expanded to 7 days for better visibility during testing.
    /// </summary>
    private const int TransitionWindowDays = 7;

    private static readonly int[] Thresholds = [31, 61, 91];

    /// <summary>
    /// Compute piutang (AR) overdue notifications for a single company.
    /// Uses transDate to calculate days since invoice.
    /// </summary>
    public static async Task<IReadOnlyList<OverdueNotificationDto>> ComputePiutangForCompany(
        string companyKey,
        DateOnly asOfDate,
        AccurateHttpClient client,
        CancellationToken ct)
    {
        string listRaw;
        try
        {
            listRaw = await client.GetSalesInvoiceListRaw(companyKey);
        }
        catch (Exception)
        {
            return [];
        }

        var ids = ParseIdsFromList(listRaw, MaxInvoiceDetails);
        if (ids.Count == 0) return [];

        var results = new ConcurrentBag<OverdueNotificationDto>();

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = ct },
            async (id, token) =>
            {
                try
                {
                    var raw = await client.GetSalesInvoiceDetailRaw(id, companyKey);
                    if (!TryParseSalesInvoiceForNotification(raw, out var detail)) return;
                    if (IsLunas(detail.StatusName)) return;
                    if (!TryParseDate(detail.TransDateRaw, out var transDate)) return;

                    var days = DaysBetween(transDate, asOfDate);
                    var bucket = GetTransitionBucket(days);
                    if (bucket == null) 
                    {
                        // Log only once per threshold type to avoid spam if needed or just log all missed ones for debug
                        // Console.WriteLine($"[Debug AR] Invoice {detail.InvoiceNumber} is {days} days old, not in transition (31, 61, 91)");
                        return;
                    }
                    Console.WriteLine($"[Notif AR] MATCH! Invoice {detail.InvoiceNumber} is {days} days old, bucket {bucket}");

                    var invoiceNumber = detail.InvoiceNumber ?? $"SI-{id}";
                    var severity = BucketToSeverity(bucket.Value);
                    var thresholdLabel = bucket.Value == 31 ? "30" : bucket.Value == 61 ? "60" : "90";

                    results.Add(new OverdueNotificationDto
                    {
                        Id = $"ar-{invoiceNumber}-{bucket.Value}",
                        Type = "piutang",
                        Severity = severity,
                        Title = $"Piutang Jatuh Tempo — {companyKey}",
                        Message = $"Invoice #{invoiceNumber} senilai {FormatRupiah(detail.TotalAmount)} sudah melewati {thresholdLabel} hari. Segera follow-up.",
                        InvoiceNumber = invoiceNumber,
                        TotalAmount = detail.TotalAmount,
                        EntityName = companyKey,
                        CounterpartyName = null,
                        DueDate = transDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DaysPastDue = days,
                        AgingBucket = bucket.Value,
                        CreatedAt = DateTime.UtcNow,
                        Category = "alert",
                    });
                }
                catch
                {
                    // Skip individual invoice failures silently
                }
            });

        return results.OrderByDescending(n => n.AgingBucket).ThenBy(n => n.EntityName).ToList();
    }

    /// <summary>
    /// Compute utang (AP) overdue notifications for a single company.
    /// Uses dueDate to calculate days since invoice due.
    /// </summary>
    public static async Task<IReadOnlyList<OverdueNotificationDto>> ComputeUtangForCompany(
        string companyKey,
        DateOnly asOfDate,
        IAccurateService service,
        CancellationToken ct)
    {
        string listRaw;
        try
        {
            listRaw = await service.GetPurchaseInvoiceListRaw(companyKey);
        }
        catch (Exception)
        {
            return [];
        }

        var ids = ParseIdsFromList(listRaw, MaxInvoiceDetails);
        if (ids.Count == 0) return [];

        var vendorNameCache = new ConcurrentDictionary<long, string>();
        var results = new ConcurrentBag<OverdueNotificationDto>();

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = ct },
            async (id, token) =>
            {
                try
                {
                    var raw = await service.GetPurchaseInvoiceDetailRaw(id, companyKey);
                    if (!TryParsePurchaseInvoiceForNotification(raw, out var detail)) return;
                    if (!IsBelumLunas(detail.StatusOutstandingRaw)) return;
                    if (!TryParseDate(detail.DueDateRaw, out var dueDate)) return;

                    var days = DaysBetween(dueDate, asOfDate);
                    var bucket = GetTransitionBucket(days);
                    if (bucket == null)
                    {
                        // Console.WriteLine($"[Debug AP] Invoice {detail.InvoiceNumber} is {days} days old, not in transition");
                        return;
                    }
                    Console.WriteLine($"[Notif AP] MATCH! Invoice {detail.InvoiceNumber} is {days} days old, bucket {bucket}");

                    // Resolve vendor name
                    var vendorName = detail.VendorName;
                    if (detail.VendorId != null && string.IsNullOrWhiteSpace(vendorName))
                    {
                        vendorName = await ResolveVendorName(service, companyKey, detail.VendorId.Value, vendorNameCache);
                    }
                    if (string.IsNullOrWhiteSpace(vendorName)) vendorName = "—";

                    var invoiceNumber = detail.InvoiceNumber ?? $"PI-{id}";
                    var severity = BucketToSeverity(bucket.Value);
                    var thresholdLabel = bucket.Value == 31 ? "30" : bucket.Value == 61 ? "60" : "90";

                    results.Add(new OverdueNotificationDto
                    {
                        Id = $"ap-{invoiceNumber}-{bucket.Value}",
                        Type = "utang",
                        Severity = severity,
                        Title = $"Utang Jatuh Tempo — {companyKey}",
                        Message = $"Invoice #{invoiceNumber} senilai {FormatRupiah(detail.PurchaseAmount)} sudah melewati {thresholdLabel} hari. Segera follow-up.",
                        InvoiceNumber = invoiceNumber,
                        TotalAmount = detail.PurchaseAmount,
                        EntityName = companyKey,
                        CounterpartyName = vendorName,
                        DueDate = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DaysPastDue = days,
                        AgingBucket = bucket.Value,
                        CreatedAt = DateTime.UtcNow,
                        Category = "alert",
                    });
                }
                catch
                {
                    // Skip individual invoice failures silently
                }
            });

        return results.OrderByDescending(n => n.AgingBucket).ThenBy(n => n.EntityName).ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the bucket threshold (31, 61, 91) if the day count falls within a transition window.
    /// Returns null if the invoice is not in any transition window.
    /// </summary>
    private static int? GetTransitionBucket(int days)
    {
        if (days >= 91) return 91;
        if (days >= 61) return 61;
        if (days >= 31) return 31;
        return null;
    }

    private static string BucketToSeverity(int bucket) => bucket switch
    {
        31 => "success",   // hijau
        61 => "warning",   // kuning
        91 => "danger",    // merah
        _ => "info",
    };

    private static int DaysBetween(DateOnly fromDate, DateOnly asOfDate)
    {
        return asOfDate.DayNumber - fromDate.DayNumber;
    }

    private static string FormatRupiah(decimal amount)
    {
        if (amount == 0) return "Rp 0";
        return $"Rp {amount:N0}".Replace(",", ".");
    }

    // ── Sales Invoice (Piutang) Parsing ─────────────────────────────────

    private sealed class SalesInvoiceNotifDto
    {
        public string? InvoiceNumber { get; init; }
        public decimal TotalAmount { get; init; }
        public string? StatusName { get; init; }
        public string? TransDateRaw { get; init; }
    }

    private static bool TryParseSalesInvoiceForNotification(string raw, out SalesInvoiceNotifDto detail)
    {
        detail = null!;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
                return false;

            string? invoiceNumber = null;
            if (d.TryGetProperty("number", out var num) && num.ValueKind == JsonValueKind.String)
                invoiceNumber = num.GetString();

            decimal totalAmount = 0;
            if (d.TryGetProperty("totalAmount", out var tamt))
            {
                totalAmount = tamt.ValueKind switch
                {
                    JsonValueKind.Number => tamt.GetDecimal(),
                    JsonValueKind.String when decimal.TryParse(tamt.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
                    _ => 0,
                };
            }

            string? statusName = null;
            if (d.TryGetProperty("statusName", out var sn) && sn.ValueKind == JsonValueKind.String)
                statusName = sn.GetString();

            string? transDate = null;
            if (d.TryGetProperty("transDate", out var td) && td.ValueKind == JsonValueKind.String)
                transDate = td.GetString();

            detail = new SalesInvoiceNotifDto
            {
                InvoiceNumber = invoiceNumber,
                TotalAmount = totalAmount,
                StatusName = statusName,
                TransDateRaw = transDate,
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns true if the invoice status indicates it IS paid (lunas).</summary>
    private static bool IsLunas(string? statusName)
    {
        if (string.IsNullOrWhiteSpace(statusName)) return false;
        var lower = statusName.Trim().ToLowerInvariant();
        if (lower.Contains("lunas", StringComparison.Ordinal) && !lower.Contains("belum", StringComparison.Ordinal))
            return true;
        if (string.Equals(statusName.Trim(), "Lunas", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    // ── Purchase Invoice (Utang) Parsing ────────────────────────────────

    private sealed class PurchaseInvoiceNotifDto
    {
        public string? InvoiceNumber { get; init; }
        public long? VendorId { get; init; }
        public string VendorName { get; init; } = "";
        public decimal PurchaseAmount { get; init; }
        public string? StatusOutstandingRaw { get; init; }
        public string? DueDateRaw { get; init; }
    }

    private static bool TryParsePurchaseInvoiceForNotification(string raw, out PurchaseInvoiceNotifDto detail)
    {
        detail = null!;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
                return false;

            string? invoiceNumber = null;
            if (d.TryGetProperty("number", out var num) && num.ValueKind == JsonValueKind.String)
                invoiceNumber = num.GetString();

            long? vendorId = null;
            var vendorName = "";
            if (d.TryGetProperty("vendor", out var vendor))
            {
                if (vendor.ValueKind == JsonValueKind.Object)
                {
                    if (vendor.TryGetProperty("id", out var vid))
                    {
                        if (vid.ValueKind == JsonValueKind.Number)
                            vendorId = vid.GetInt64();
                        else if (vid.ValueKind == JsonValueKind.String && long.TryParse(vid.GetString(), out var lp))
                            vendorId = lp;
                    }
                    if (vendor.TryGetProperty("name", out var vn) && vn.ValueKind == JsonValueKind.String)
                        vendorName = vn.GetString() ?? "";
                }
                else if (vendor.ValueKind == JsonValueKind.String)
                {
                    vendorName = vendor.GetString() ?? "";
                }
            }
            // Also check vendorId at root level
            if (vendorId == null && d.TryGetProperty("vendorId", out var rootVid))
            {
                if (rootVid.ValueKind == JsonValueKind.Number)
                    vendorId = rootVid.GetInt64();
                else if (rootVid.ValueKind == JsonValueKind.String && long.TryParse(rootVid.GetString(), out var lp2))
                    vendorId = lp2;
            }

            decimal purchaseAmount = 0;
            if (d.TryGetProperty("purchaseAmount", out var pa))
            {
                purchaseAmount = pa.ValueKind switch
                {
                    JsonValueKind.Number => pa.GetDecimal(),
                    JsonValueKind.String when decimal.TryParse(pa.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
                    _ => 0,
                };
            }

            string? statusOutstanding = null;
            if (d.TryGetProperty("statusOutstanding", out var so))
            {
                statusOutstanding = so.ValueKind switch
                {
                    JsonValueKind.String => so.GetString(),
                    JsonValueKind.Number => so.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null,
                };
            }

            string? dueDate = null;
            if (d.TryGetProperty("dueDate", out var dd) && dd.ValueKind == JsonValueKind.String)
                dueDate = dd.GetString();

            detail = new PurchaseInvoiceNotifDto
            {
                InvoiceNumber = invoiceNumber,
                VendorId = vendorId,
                VendorName = vendorName,
                PurchaseAmount = purchaseAmount,
                StatusOutstandingRaw = statusOutstanding,
                DueDateRaw = dueDate,
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBelumLunas(string? statusOutstandingRaw)
    {
        if (string.IsNullOrWhiteSpace(statusOutstandingRaw)) return true;
        var s = statusOutstandingRaw.Trim().ToLowerInvariant();
        if (s == "false" || s == "0" || s == "0.0") return false;
        if (s.Contains("belum", StringComparison.Ordinal) && s.Contains("lunas", StringComparison.Ordinal)) return true;
        if (s.Contains("open", StringComparison.Ordinal) || s.Contains("outstanding", StringComparison.Ordinal)) return true;
        if (s.Contains("true", StringComparison.Ordinal) || s == "1" || s == "1.0") return true;
        if (s.Contains("lunas", StringComparison.Ordinal) || s.Contains("paid", StringComparison.Ordinal) || s.Contains("settled", StringComparison.Ordinal))
            return false;
        return true;
    }

    // ── Shared Parsing ──────────────────────────────────────────────────

    private static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd" })
        {
            if (DateOnly.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }
        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static List<string> ParseIdsFromList(string listRaw, int maxCount)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(listRaw);
            if (!doc.RootElement.TryGetProperty("d", out var d))
                return result;

            if (d.ValueKind == JsonValueKind.Array)
            {
                CollectIds(d, result, maxCount);
                return result;
            }
            if (d.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "rows", "data", "result", "items" })
                {
                    if (!d.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
                        continue;
                    CollectIds(arr, result, maxCount);
                    if (result.Count > 0) return result;
                }
            }
        }
        catch { /* ignore parse errors */ }
        return result;
    }

    private static void CollectIds(JsonElement array, List<string> result, int maxCount)
    {
        foreach (var el in array.EnumerateArray())
        {
            if (result.Count >= maxCount) break;
            if (!el.TryGetProperty("id", out var idEl)) continue;
            var id = idEl.ValueKind switch
            {
                JsonValueKind.Number => idEl.GetRawText(),
                JsonValueKind.String => idEl.GetString(),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(id)) result.Add(id!);
        }
    }

    private static async Task<string> ResolveVendorName(
        IAccurateService service,
        string companyKey,
        long vendorId,
        ConcurrentDictionary<long, string> cache)
    {
        if (cache.TryGetValue(vendorId, out var cached)) return cached;
        try
        {
            var raw = await service.GetVendorDetailRaw(vendorId.ToString(CultureInfo.InvariantCulture), companyKey);
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                if (d.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                {
                    var name = nm.GetString()?.Trim() ?? "—";
                    cache[vendorId] = name;
                    return name;
                }
            }
        }
        catch { /* ignore */ }
        cache[vendorId] = "—";
        return "—";
    }
}
