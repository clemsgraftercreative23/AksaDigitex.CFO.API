using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.UtangPiutang;

internal sealed record ArCustomerRow(
    string Company,
    long? CustomerId,
    string CustomerName,
    decimal Outstanding,
    decimal Bucket0_30,
    decimal Bucket31_60,
    decimal Bucket61_90,
    decimal BucketOver90,
    string Status);

/// <summary>
/// Agregasi AR aging dari <c>sales-invoice/list.do</c> + <c>detail.do</c> per entitas Accurate.
/// </summary>
internal static class ArAgingComputation
{
    public const int MaxInvoiceDetailsPerCompany = 500;
    public const int MaxDetailParallelism = 6;

    public static async Task<IReadOnlyList<ArCustomerRow>> ComputeForCompany(
        string companyKey,
        DateOnly asOfDate,
        AccurateHttpClient client,
        CancellationToken cancellationToken)
    {
        var listRaw = await client.GetSalesInvoiceListRaw(companyKey);
        var ids = ParseInvoiceIdsFromList(listRaw, MaxInvoiceDetailsPerCompany);
        if (ids.Count == 0)
            return Array.Empty<ArCustomerRow>();

        var aggregates = new ConcurrentDictionary<string, CustomerAggregate>(StringComparer.Ordinal);

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
                    detailRaw = await client.GetSalesInvoiceDetailRaw(id, companyKey);
                }
                catch
                {
                    return;
                }

                if (!TryParseInvoiceDetail(detailRaw, out var detail))
                    return;

                if (!IsUnpaidInvoice(detail.StatusName))
                    return;

                if (!TryParseAccurateDate(detail.TransDateRaw, out var transDate))
                    return;

                var asOfDt = asOfDate.ToDateTime(TimeOnly.MinValue);
                var transDt = transDate.ToDateTime(TimeOnly.MinValue);
                var days = (int)Math.Floor((asOfDt - transDt).TotalDays);
                if (days < 0)
                    return;

                var amount = detail.TotalAmount;
                var custId = detail.CustomerId;
                var custName = string.IsNullOrWhiteSpace(detail.CustomerName) ? "—" : detail.CustomerName.Trim();
                var aggKey = $"{companyKey}\u001f{custId?.ToString(CultureInfo.InvariantCulture) ?? custName}";

                aggregates.AddOrUpdate(
                    aggKey,
                    _ => CustomerAggregate.Create(companyKey, custId, custName, amount, days),
                    (_, existing) =>
                    {
                        existing.Add(amount, days);
                        return existing;
                    });
            });

        return aggregates.Values
            .Select(a => a.ToRow())
            .OrderBy(r => r.CustomerName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ParseInvoiceIdsFromList(string listRaw, int maxCount)
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
            if (result.Count >= maxCount)
                break;
            var id = ExtractIdString(el);
            if (id != null)
                result.Add(id);
        }
    }

    private static string? ExtractIdString(JsonElement el)
    {
        if (!el.TryGetProperty("id", out var idEl))
            return null;
        return idEl.ValueKind switch
        {
            JsonValueKind.Number => idEl.GetRawText(),
            JsonValueKind.String => idEl.GetString(),
            _ => null,
        };
    }

    private sealed class InvoiceDetailDto
    {
        public long? CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public decimal TotalAmount { get; init; }
        public string? StatusName { get; init; }
        public string? TransDateRaw { get; init; }
    }

    private static bool TryParseInvoiceDetail(string raw, out InvoiceDetailDto detail)
    {
        detail = null!;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object)
                return false;

            long? customerId = null;
            var customerName = "";
            if (d.TryGetProperty("customer", out var cust))
            {
                if (cust.ValueKind == JsonValueKind.Object)
                {
                    if (cust.TryGetProperty("id", out var cid))
                    {
                        if (cid.ValueKind == JsonValueKind.Number)
                            customerId = cid.GetInt64();
                        else if (cid.ValueKind == JsonValueKind.String && long.TryParse(cid.GetString(), out var lp))
                            customerId = lp;
                    }

                    if (cust.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String)
                        customerName = cn.GetString() ?? "";
                }
                else if (cust.ValueKind == JsonValueKind.String)
                {
                    customerName = cust.GetString() ?? "";
                }
            }

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

            detail = new InvoiceDetailDto
            {
                CustomerId = customerId,
                CustomerName = customerName,
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

    /// <summary>Dianggap belum lunas kecuali status jelas "lunas" tanpa "belum".</summary>
    internal static bool IsUnpaidInvoice(string? statusName)
    {
        if (string.IsNullOrWhiteSpace(statusName))
            return true;
        var s = statusName.Trim();
        var lower = s.ToLowerInvariant();
        if (lower.Contains("lunas", StringComparison.Ordinal) && !lower.Contains("belum", StringComparison.Ordinal))
            return false;
        if (string.Equals(s, "Lunas", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool TryParseAccurateDate(string? raw, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var trimmed = raw.Trim();
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy" })
        {
            if (DateOnly.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return true;
        }

        return DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private sealed class CustomerAggregate
    {
        public required string Company { get; init; }
        public long? CustomerId { get; init; }
        public string CustomerName { get; set; } = "";

        public decimal Outstanding { get; private set; }
        public decimal B0_30 { get; private set; }
        public decimal B31_60 { get; private set; }
        public decimal B61_90 { get; private set; }
        public decimal B90 { get; private set; }

        public static CustomerAggregate Create(
            string company,
            long? customerId,
            string customerName,
            decimal amount,
            int days)
        {
            var a = new CustomerAggregate
            {
                Company = company,
                CustomerId = customerId,
                CustomerName = customerName,
            };
            a.Add(amount, days);
            return a;
        }

        public void Add(decimal amount, int days)
        {
            Outstanding += amount;
            if (days <= 30)
                B0_30 += amount;
            else if (days <= 60)
                B31_60 += amount;
            else if (days <= 90)
                B61_90 += amount;
            else
                B90 += amount;
        }

        public ArCustomerRow ToRow()
        {
            var status = DeriveStatus(B61_90, B90);
            return new ArCustomerRow(
                Company,
                CustomerId,
                CustomerName,
                Outstanding,
                B0_30,
                B31_60,
                B61_90,
                B90,
                status);
        }

        private static string DeriveStatus(decimal b61, decimal b90)
        {
            if (b90 > 0)
                return "Overdue";
            if (b61 > 0)
                return "Perhatian";
            return "Normal";
        }
    }
}
