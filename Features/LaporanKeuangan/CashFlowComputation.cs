using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using MyBackend.Application.Services;

namespace MyBackend.Features.LaporanKeuangan;

internal sealed record CashFlowMonthlyRow(
    string Month,
    decimal CashIn,
    decimal CashOut,
    decimal Net,
    decimal Cumulative);

internal sealed record CashFlowComputationResult(
    IReadOnlyList<CashFlowMonthlyRow> Rows,
    decimal TotalCashIn,
    decimal TotalCashOut,
    decimal NetCashFlow);

internal static class CashFlowComputation
{
    private const int MaxIdsPerCompany = 500;
    private const int MaxParallelism = 6;

    public static async Task<CashFlowComputationResult> ComputeAsync(
        IReadOnlyList<string> companyKeys,
        DateOnly fromDate,
        DateOnly toDate,
        IAccurateService service,
        CancellationToken cancellationToken)
    {
        var monthMap = new ConcurrentDictionary<string, (decimal CashIn, decimal CashOut)>(StringComparer.Ordinal);

        foreach (var company in companyKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AccumulateCashInForCompany(company, fromDate, toDate, service, monthMap, cancellationToken);
            await AccumulateCashOutForCompany(company, fromDate, toDate, service, monthMap, cancellationToken);
        }

        var months = EnumerateMonths(fromDate, toDate);
        var rows = new List<CashFlowMonthlyRow>(months.Count);
        decimal cumulative = 0;
        decimal totalIn = 0;
        decimal totalOut = 0;

        foreach (var m in months)
        {
            var v = monthMap.TryGetValue(m, out var value) ? value : (CashIn: 0m, CashOut: 0m);
            var net = v.CashIn - v.CashOut;
            cumulative += net;
            totalIn += v.CashIn;
            totalOut += v.CashOut;
            rows.Add(new CashFlowMonthlyRow(m, v.CashIn, v.CashOut, net, cumulative));
        }

        return new CashFlowComputationResult(
            rows,
            TotalCashIn: totalIn,
            TotalCashOut: totalOut,
            NetCashFlow: totalIn - totalOut);
    }

    private static async Task AccumulateCashInForCompany(
        string company,
        DateOnly fromDate,
        DateOnly toDate,
        IAccurateService service,
        ConcurrentDictionary<string, (decimal CashIn, decimal CashOut)> monthMap,
        CancellationToken cancellationToken)
    {
        var listRaw = await service.GetOtherDepositListRaw(company);
        var ids = ParseIdsFromList(listRaw, MaxIdsPerCompany);
        if (ids.Count == 0) return;

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (id, token) =>
            {
                string raw;
                try { raw = await service.GetOtherDepositDetailRaw(id, company); } catch { return; }
                if (!TryParseOtherDepositDetail(raw, out var transDate, out var amount, out var approvalStatus)) return;
                if (!IsApprovedStatus(approvalStatus)) return;
                if (transDate < fromDate || transDate > toDate) return;
                var month = transDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                monthMap.AddOrUpdate(month, (amount, 0m), (_, prev) => (prev.CashIn + amount, prev.CashOut));
            });
    }

    private static async Task AccumulateCashOutForCompany(
        string company,
        DateOnly fromDate,
        DateOnly toDate,
        IAccurateService service,
        ConcurrentDictionary<string, (decimal CashIn, decimal CashOut)> monthMap,
        CancellationToken cancellationToken)
    {
        var listRaw = await service.GetPurchaseInvoiceListRaw(company);
        var ids = ParseIdsFromList(listRaw, MaxIdsPerCompany);
        if (ids.Count == 0) return;

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (id, token) =>
            {
                string raw;
                try { raw = await service.GetPurchaseInvoiceDetailRaw(id, company); } catch { return; }
                if (!TryParsePurchaseInvoiceDetail(raw, out var transDate, out var amount, out var statusOutstanding)) return;
                if (!IsSettledStatus(statusOutstanding)) return;
                if (transDate < fromDate || transDate > toDate) return;
                var month = transDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                monthMap.AddOrUpdate(month, (0m, amount), (_, prev) => (prev.CashIn, prev.CashOut + amount));
            });
    }

    private static List<string> ParseIdsFromList(string listRaw, int maxCount)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(listRaw);
            if (!doc.RootElement.TryGetProperty("d", out var d)) return result;

            if (d.ValueKind == JsonValueKind.Array)
            {
                CollectIdsFromArray(d, result, maxCount);
                return result;
            }

            if (d.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "rows", "data", "result", "items" })
                {
                    if (!d.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
                    CollectIdsFromArray(arr, result, maxCount);
                    if (result.Count > 0) return result;
                }
            }
        }
        catch
        {
            // ignore malformed payload per entry
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

    private static bool TryParseOtherDepositDetail(
        string raw,
        out DateOnly transDate,
        out decimal amount,
        out string approvalStatus)
    {
        transDate = default;
        amount = 0m;
        approvalStatus = "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object) return false;

            if (!TryReadDate(d, "transDate", out transDate)) return false;
            amount = ReadDecimal(d, "amount");
            approvalStatus = ReadStatusLike(d, new[] { "approvalStatus" });
            return amount > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParsePurchaseInvoiceDetail(
        string raw,
        out DateOnly transDate,
        out decimal purchaseAmount,
        out string statusOutstanding)
    {
        transDate = default;
        purchaseAmount = 0m;
        statusOutstanding = "";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Object) return false;

            if (!TryReadDate(d, "transDate", out transDate)) return false;
            purchaseAmount = ReadDecimal(d, "purchaseAmount");
            statusOutstanding = ReadStatusLike(d, new[] { "statusOutstanding", "status", "statusName" });
            return purchaseAmount > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadDate(JsonElement obj, string prop, out DateOnly result)
    {
        result = default;
        if (!obj.TryGetProperty(prop, out var dateEl) || dateEl.ValueKind != JsonValueKind.String) return false;
        var raw = dateEl.GetString();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ" })
        {
            if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)) return true;
        }

        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static decimal ReadDecimal(JsonElement obj, params string[] props)
    {
        foreach (var prop in props)
        {
            if (!obj.TryGetProperty(prop, out var el)) continue;
            var parsed = el.ValueKind switch
            {
                JsonValueKind.Number => el.GetDecimal(),
                JsonValueKind.String when decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
                _ => 0m,
            };
            if (parsed != 0m) return parsed;
        }
        return 0m;
    }

    private static string ReadStatusLike(JsonElement obj, IReadOnlyList<string> props)
    {
        foreach (var p in props)
        {
            if (!obj.TryGetProperty(p, out var el)) continue;
            if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
            if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
            if (el.ValueKind == JsonValueKind.True) return "true";
            if (el.ValueKind == JsonValueKind.False) return "false";
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var nested in new[] { "name", "status", "label", "value" })
                {
                    if (!el.TryGetProperty(nested, out var n)) continue;
                    if (n.ValueKind == JsonValueKind.String) return n.GetString() ?? "";
                    if (n.ValueKind == JsonValueKind.Number) return n.GetRawText();
                    if (n.ValueKind == JsonValueKind.True) return "true";
                    if (n.ValueKind == JsonValueKind.False) return "false";
                }
            }
        }
        return "";
    }

    private static bool IsSettledStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim().ToLowerInvariant();
        if (s == "0" || s == "0.0" || s == "false") return true;
        return s.Contains("lunas", StringComparison.Ordinal);
    }

    private static bool IsApprovedStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        return string.Equals(s, "APPROVED", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> EnumerateMonths(DateOnly fromDate, DateOnly toDate)
    {
        var result = new List<string>();
        var y = fromDate.Year;
        var m = fromDate.Month;
        while (y < toDate.Year || (y == toDate.Year && m <= toDate.Month))
        {
            result.Add($"{y:D4}-{m:D2}");
            m++;
            if (m > 12)
            {
                m = 1;
                y++;
            }
        }
        return result;
    }
}
