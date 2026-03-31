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
    internal const int DefaultMaxIdsPerCompany = 5000;
    internal const int MinMaxIdsPerCompany = 200;
    internal const int MaxMaxIdsPerCompany = 5000;
    private const int MaxParallelism = 6;

    public static async Task<CashFlowComputationResult> ComputeAsync(
        IReadOnlyList<string> companyKeys,
        DateOnly fromDate,
        DateOnly toDate,
        IAccurateService service,
        CancellationToken cancellationToken,
        int? maxIdsPerCompany = null)
    {
        var effectiveMaxIds = maxIdsPerCompany is > 0
            ? Math.Clamp(maxIdsPerCompany.Value, MinMaxIdsPerCompany, MaxMaxIdsPerCompany)
            : DefaultMaxIdsPerCompany;
        var monthMap = new ConcurrentDictionary<string, (decimal CashIn, decimal CashOut)>(StringComparer.Ordinal);

        foreach (var company in companyKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await AccumulateCashInForCompany(company, fromDate, toDate, service, monthMap, cancellationToken, effectiveMaxIds);
            await AccumulateCashOutForCompany(company, fromDate, toDate, service, monthMap, cancellationToken, effectiveMaxIds);
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
        CancellationToken cancellationToken,
        int maxIdsPerCompany)
    {
        var listRaw = await service.GetOtherDepositListRaw(company);
        var ids = ParseIdsFromList(listRaw, maxIdsPerCompany);
        if (ids.Count == 0) return;

        // Hitung SEMUA transaksi dalam rentang tanggal — tidak filter per approvalStatus.
        // Sesuai spec sheet Accurate: semua penerimaan (other-deposit) dijumlahkan.
        var allValidMonthly = new ConcurrentDictionary<string, decimal>(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (id, token) =>
            {
                string raw;
                try { raw = await service.GetOtherDepositDetailRaw(id, company); } catch { return; }
                if (!TryParseOtherDepositDetail(raw, out var transDate, out var amount, out _)) return;
                if (transDate < fromDate || transDate > toDate) return;
                var month = transDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                allValidMonthly.AddOrUpdate(month, amount, (_, prev) => prev + amount);
            });

        foreach (var (month, amount) in allValidMonthly)
        {
            monthMap.AddOrUpdate(month, (amount, 0m), (_, prev) => (prev.CashIn + amount, prev.CashOut));
        }
    }

    private static async Task AccumulateCashOutForCompany(
        string company,
        DateOnly fromDate,
        DateOnly toDate,
        IAccurateService service,
        ConcurrentDictionary<string, (decimal CashIn, decimal CashOut)> monthMap,
        CancellationToken cancellationToken,
        int maxIdsPerCompany)
    {
        var listRaw = await service.GetPurchaseInvoiceListRaw(company);
        var ids = ParseIdsFromList(listRaw, maxIdsPerCompany);
        if (ids.Count == 0) return;

        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = cancellationToken },
            async (id, token) =>
            {
                string raw;
                try { raw = await service.GetPurchaseInvoiceDetailRaw(id, company); } catch { return; }
                if (!TryParsePurchaseInvoiceDetail(raw, out var transDate, out var amount, out var statusOutstanding, out var outstandingBalance)) return;
                // Anggap lunas jika: statusOutstanding menandakan lunas ATAU sisa tagihan = 0
                var settled = IsSettledStatus(statusOutstanding)
                              || (outstandingBalance >= 0m && outstandingBalance == 0m);
                if (!settled) return;
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
            var root = doc.RootElement;
            if (root.TryGetProperty("d", out var d))
            {
                root = d;
            }

            // Accurate punya variasi struktur list (array langsung / rows / sp.rows / dsb),
            // jadi scan rekursif untuk properti "id" agar cash-in tidak kosong.
            CollectIdsRecursively(root, result, maxCount);
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

    private static void CollectIdsRecursively(JsonElement node, List<string> result, int maxCount)
    {
        if (result.Count >= maxCount) return;

        if (node.ValueKind == JsonValueKind.Array)
        {
            CollectIdsFromArray(node, result, maxCount);
            foreach (var child in node.EnumerateArray())
            {
                if (result.Count >= maxCount) break;
                if (child.ValueKind == JsonValueKind.Object || child.ValueKind == JsonValueKind.Array)
                    CollectIdsRecursively(child, result, maxCount);
            }
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) return;

        foreach (var prop in node.EnumerateObject())
        {
            if (result.Count >= maxCount) break;
            var child = prop.Value;
            if (child.ValueKind == JsonValueKind.Array || child.ValueKind == JsonValueKind.Object)
                CollectIdsRecursively(child, result, maxCount);
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
            if (!TryGetPayloadObject(doc.RootElement, out var d)) return false;

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
        out string statusOutstanding,
        out decimal outstandingBalance)
    {
        transDate = default;
        purchaseAmount = 0m;
        statusOutstanding = "";
        outstandingBalance = -1m; // -1 = tidak tersedia di response
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!TryGetPayloadObject(doc.RootElement, out var d)) return false;

            if (!TryReadDate(d, "transDate", out transDate)) return false;

            // Coba berbagai nama field amount yang dipakai Accurate
            purchaseAmount = ReadDecimal(d, "purchaseAmount", "totalAmount", "amount");

            statusOutstanding = ReadStatusLike(d, new[] { "statusOutstanding", "status", "statusName" });

            // Cek apakah ada field sisa tagihan — jika 0, sudah lunas
            var balanceRaw = ReadDecimal(d, "totalBalance", "outstandingAmount", "remainingAmount", "balanceDue");
            if (balanceRaw >= 0)
                outstandingBalance = balanceRaw;

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
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ" })
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
                JsonValueKind.Object => ReadDecimal(el, "value", "amount", "total"),
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
        // null/empty → tidak ada sisa tagihan = sudah lunas di sebagian respon Accurate
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var s = raw.Trim().ToLowerInvariant();
        // Nilai numerik/boolean yang berarti "tidak ada outstanding"
        if (s == "0" || s == "0.0" || s == "false") return true;
        // Kata kunci status lunas dalam berbagai bahasa/format Accurate
        if (s.Contains("lunas", StringComparison.Ordinal)) return true;
        if (s.Contains("paid", StringComparison.Ordinal)) return true;
        if (s.Contains("settled", StringComparison.Ordinal)) return true;
        if (s.Contains("close", StringComparison.Ordinal)) return true; // CLOSED
        if (s.Contains("done", StringComparison.Ordinal)) return true;
        if (s.Contains("complete", StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool IsApprovedStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim().ToLowerInvariant();
        if (s is "1" or "true") return true;
        return s.Contains("approved", StringComparison.Ordinal);
    }

    private static bool TryGetPayloadObject(JsonElement root, out JsonElement payload)
    {
        payload = default;
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("d", out var d))
            {
                if (d.ValueKind == JsonValueKind.Object)
                {
                    payload = d;
                    return true;
                }
                return false;
            }

            // Beberapa endpoint bisa langsung kirim object tanpa envelope "d".
            payload = root;
            return true;
        }
        return false;
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
