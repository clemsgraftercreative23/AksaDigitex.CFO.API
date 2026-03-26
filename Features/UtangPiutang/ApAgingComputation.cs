using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MyBackend.Application.Services;

namespace MyBackend.Features.UtangPiutang;

internal sealed record ApVendorRow(
    string Company,
    long? VendorId,
    string VendorName,
    decimal Outstanding,
    decimal Bucket0_30,
    decimal Bucket31_60,
    decimal Bucket61_90,
    decimal BucketOver90,
    string Status);

internal static class ApAgingComputation
{
    public const int MaxInvoiceDetailsPerCompany = 500;
    public const int MaxDetailParallelism = 6;

    public static async Task<IReadOnlyList<ApVendorRow>> ComputeForCompany(
        string companyKey,
        DateOnly asOfDate,
        IAccurateService service,
        CancellationToken cancellationToken)
    {
        var listRaw = await service.GetPurchaseInvoiceListRaw(companyKey);
        var ids = ParseIdsFromList(listRaw, MaxInvoiceDetailsPerCompany);
        if (ids.Count == 0) return Array.Empty<ApVendorRow>();

        var vendorNameCache = new ConcurrentDictionary<long, string>();
        var aggregates = new ConcurrentDictionary<string, VendorAggregate>(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDetailParallelism,
                CancellationToken = cancellationToken,
            },
            async (id, token) =>
            {
                string detailRaw;
                try
                {
                    detailRaw = await service.GetPurchaseInvoiceDetailRaw(id, companyKey);
                }
                catch
                {
                    return;
                }

                if (!TryParsePurchaseInvoiceDetail(detailRaw, out var detail))
                    return;

                if (!IsBelumLunas(detail.StatusOutstandingRaw))
                    return;

                if (!TryParseAccurateDate(detail.DueDateRaw, out var dueDate))
                    return;

                var asOfDt = asOfDate.ToDateTime(TimeOnly.MinValue);
                var dueDt = dueDate.ToDateTime(TimeOnly.MinValue);
                var days = (int)Math.Floor((asOfDt - dueDt).TotalDays);
                if (days < 0)
                    return;

                var amount = detail.PurchaseAmount;
                if (amount <= 0) return;

                var vendorId = detail.VendorId;
                var vendorName = detail.VendorName;
                if (vendorId != null)
                {
                    vendorName = await ResolveVendorName(service, companyKey, vendorId.Value, vendorName, vendorNameCache);
                }
                if (string.IsNullOrWhiteSpace(vendorName)) vendorName = "—";

                var key = $"{companyKey}\u001f{vendorId?.ToString(CultureInfo.InvariantCulture) ?? vendorName}";
                aggregates.AddOrUpdate(
                    key,
                    _ => VendorAggregate.Create(companyKey, vendorId, vendorName, amount, days),
                    (_, existing) =>
                    {
                        existing.Add(amount, days);
                        return existing;
                    });
            });

        return aggregates.Values
            .Select(v => v.ToRow())
            .OrderBy(v => v.VendorName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<string> ResolveVendorName(
        IAccurateService service,
        string companyKey,
        long vendorId,
        string fallbackName,
        ConcurrentDictionary<long, string> cache)
    {
        if (cache.TryGetValue(vendorId, out var nameFromCache)) return nameFromCache;
        try
        {
            var raw = await service.GetVendorDetailRaw(vendorId.ToString(CultureInfo.InvariantCulture), companyKey);
            if (TryParseVendorName(raw, out var vendorName) && !string.IsNullOrWhiteSpace(vendorName))
            {
                cache[vendorId] = vendorName;
                return vendorName;
            }
        }
        catch
        {
            // ignore vendor detail failure
        }

        var fallback = string.IsNullOrWhiteSpace(fallbackName) ? "—" : fallbackName.Trim();
        cache[vendorId] = fallback;
        return fallback;
    }

    private static bool TryParseVendorName(string raw, out string vendorName)
    {
        vendorName = "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
                return false;
            if (d.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
            {
                vendorName = nm.GetString() ?? "";
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
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
                CollectIdsFromArray(d, result, maxCount);
                return result;
            }

            if (d.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "rows", "data", "result", "items" })
                {
                    if (!d.TryGetProperty(propName, out var arr) || arr.ValueKind != JsonValueKind.Array)
                        continue;
                    CollectIdsFromArray(arr, result, maxCount);
                    if (result.Count > 0)
                        return result;
                }
            }
        }
        catch
        {
            // ignore
        }

        return result;
    }

    private static void CollectIdsFromArray(JsonElement array, List<string> result, int maxCount)
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
            if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
        }
    }

    private sealed class PurchaseInvoiceDetailDto
    {
        public long? VendorId { get; init; }
        public string VendorName { get; init; } = "";
        public decimal PurchaseAmount { get; init; }
        public string? StatusOutstandingRaw { get; init; }
        public string? DueDateRaw { get; init; }
    }

    private static bool TryParsePurchaseInvoiceDetail(string raw, out PurchaseInvoiceDetailDto detail)
    {
        detail = null!;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
                return false;

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
                        else if (vid.ValueKind == JsonValueKind.String && long.TryParse(vid.GetString(), out var parsed))
                            vendorId = parsed;
                    }

                    if (vendor.TryGetProperty("name", out var vn) && vn.ValueKind == JsonValueKind.String)
                        vendorName = vn.GetString() ?? "";
                }
                else if (vendor.ValueKind == JsonValueKind.String)
                {
                    vendorName = vendor.GetString() ?? "";
                }
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
                if (so.ValueKind == JsonValueKind.String) statusOutstanding = so.GetString();
                else if (so.ValueKind == JsonValueKind.Number) statusOutstanding = so.GetRawText();
                else if (so.ValueKind == JsonValueKind.True) statusOutstanding = "true";
                else if (so.ValueKind == JsonValueKind.False) statusOutstanding = "false";
            }

            string? dueDate = null;
            if (d.TryGetProperty("dueDate", out var dd) && dd.ValueKind == JsonValueKind.String)
                dueDate = dd.GetString();

            detail = new PurchaseInvoiceDetailDto
            {
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

    private static bool TryParseAccurateDate(string? raw, out DateOnly date)
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

    private sealed class VendorAggregate
    {
        public required string Company { get; init; }
        public long? VendorId { get; init; }
        public string VendorName { get; set; } = "";
        public decimal Outstanding { get; private set; }
        public decimal B0_30 { get; private set; }
        public decimal B31_60 { get; private set; }
        public decimal B61_90 { get; private set; }
        public decimal B90 { get; private set; }

        public static VendorAggregate Create(
            string company,
            long? vendorId,
            string vendorName,
            decimal amount,
            int days)
        {
            var a = new VendorAggregate
            {
                Company = company,
                VendorId = vendorId,
                VendorName = vendorName,
            };
            a.Add(amount, days);
            return a;
        }

        public void Add(decimal amount, int days)
        {
            Outstanding += amount;
            if (days <= 30) B0_30 += amount;
            else if (days <= 60) B31_60 += amount;
            else if (days <= 90) B61_90 += amount;
            else B90 += amount;
        }

        public ApVendorRow ToRow()
        {
            var status = B90 > 0 ? "Overdue" : (B61_90 > 0 ? "Perhatian" : "Normal");
            return new ApVendorRow(
                Company,
                VendorId,
                VendorName,
                Outstanding,
                B0_30,
                B31_60,
                B61_90,
                B90,
                status);
        }
    }
}
