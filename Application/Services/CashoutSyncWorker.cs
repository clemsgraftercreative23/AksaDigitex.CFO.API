using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyBackend.Infrastructure.Persistence;

namespace MyBackend.Application.Services;

public sealed class CashoutSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICashoutSyncQueue _queue;
    private readonly ILogger<CashoutSyncWorker> _logger;

    public CashoutSyncWorker(
        IServiceScopeFactory scopeFactory,
        ICashoutSyncQueue queue,
        ILogger<CashoutSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            CashoutSyncJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessJobAsync(job, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CashoutSync] Job failed");
            }
        }
    }

    private async Task ProcessJobAsync(CashoutSyncJob job, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CfoDbContext>();
        var accurate = scope.ServiceProvider.GetRequiredService<IAccurateService>();

        foreach (var company in job.Companies)
        {
            await MarkSyncStatusAsync(db, company, job.FromDate, job.ToDate, "running", cancellationToken);

            try
            {
                var listRaw = await accurate.GetOtherPaymentListRaw(company);
                var ids = ParseIds(listRaw, maxCount: 30000);
                var dailyMap = new Dictionary<DateOnly, (decimal Amount, int Count)>();
                for (var d = job.FromDate; d <= job.ToDate; d = d.AddDays(1))
                    dailyMap[d] = (0m, 0);

                foreach (var id in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string detailRaw;
                    try
                    {
                        detailRaw = await accurate.GetOtherPaymentDetailRaw(id, company);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!TryParseOtherPaymentDetail(detailRaw, out var transDate, out var amount, out var approvalStatus))
                        continue;
                    if (!IsApprovedStatus(approvalStatus))
                        continue;
                    if (transDate < job.FromDate || transDate > job.ToDate)
                        continue;

                    var v = dailyMap[transDate];
                    dailyMap[transDate] = (v.Amount + amount, v.Count + 1);
                }

                foreach (var (date, v) in dailyMap)
                {
                    await UpsertCacheRowAsync(db, company, date, "other-payment", v.Amount, v.Count, false, cancellationToken);
                }

                await MarkSyncStatusAsync(db, company, job.FromDate, job.ToDate, "done", cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CashoutSync] failed company={Company}", company);
                await MarkSyncStatusAsync(db, company, job.FromDate, job.ToDate, "error", cancellationToken);
            }
        }
    }

    private static async Task UpsertCacheRowAsync(
        CfoDbContext db,
        string company,
        DateOnly periodDate,
        string source,
        decimal totalAmount,
        int recordCount,
        bool isPartial,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO accurate_cashout_cache (company, period_date, source, total_amount, record_count, cached_at, is_partial)
            VALUES ({company}, {periodDate}, {source}, {totalAmount}, {recordCount}, {DateTime.UtcNow}, {isPartial})
            ON CONFLICT (company, period_date, source)
            DO UPDATE SET
              total_amount = EXCLUDED.total_amount,
              record_count = EXCLUDED.record_count,
              cached_at = EXCLUDED.cached_at,
              is_partial = EXCLUDED.is_partial
        ", cancellationToken);
    }

    private static async Task MarkSyncStatusAsync(
        CfoDbContext db,
        string company,
        DateOnly fromDate,
        DateOnly toDate,
        string status,
        CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO accurate_sync_status (company, last_full_sync, last_sync_from, last_sync_to, status)
            VALUES ({company}, {DateTime.UtcNow}, {fromDate}, {toDate}, {status})
            ON CONFLICT (company)
            DO UPDATE SET
              last_full_sync = EXCLUDED.last_full_sync,
              last_sync_from = EXCLUDED.last_sync_from,
              last_sync_to = EXCLUDED.last_sync_to,
              status = EXCLUDED.status
        ", cancellationToken);
    }

    private static List<string> ParseIds(string listRaw, int maxCount)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(listRaw);
            var root = doc.RootElement;
            if (root.TryGetProperty("d", out var d))
                root = d;
            CollectIdsRecursively(root, result, maxCount);
        }
        catch
        {
            // ignore parse error
        }
        return result;
    }

    private static void CollectIdsRecursively(JsonElement node, List<string> result, int maxCount)
    {
        if (result.Count >= maxCount) return;
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in node.EnumerateArray())
            {
                if (result.Count >= maxCount) break;
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("id", out var idEl))
                {
                    var id = idEl.ValueKind switch
                    {
                        JsonValueKind.Number => idEl.GetRawText(),
                        JsonValueKind.String => idEl.GetString(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
                }
                if (el.ValueKind == JsonValueKind.Array || el.ValueKind == JsonValueKind.Object)
                    CollectIdsRecursively(el, result, maxCount);
            }
            return;
        }

        if (node.ValueKind != JsonValueKind.Object) return;
        foreach (var p in node.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array || p.Value.ValueKind == JsonValueKind.Object)
                CollectIdsRecursively(p.Value, result, maxCount);
        }
    }

    private static bool TryParseOtherPaymentDetail(
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
            amount = ReadDecimal(d, "amount", "paymentAmount", "totalPayment");
            approvalStatus = ReadStatusLike(d, new[] { "approvalStatus", "status", "statusName" });
            return amount > 0m;
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
        var s = (dateEl.GetString() ?? "").Trim();
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy/MM/dd" })
        {
            if (DateOnly.TryParseExact(s, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
                return true;
        }
        return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static decimal ReadDecimal(JsonElement obj, params string[] props)
    {
        foreach (var p in props)
        {
            if (!obj.TryGetProperty(p, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;
            if (el.ValueKind == JsonValueKind.String &&
                decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var ds)) return ds;
        }
        return 0m;
    }

    private static string ReadStatusLike(JsonElement obj, IReadOnlyList<string> props)
    {
        foreach (var p in props)
        {
            if (!obj.TryGetProperty(p, out var el)) continue;
            if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
            if (el.ValueKind == JsonValueKind.True) return "true";
            if (el.ValueKind == JsonValueKind.False) return "false";
            if (el.ValueKind == JsonValueKind.Number) return el.GetRawText();
        }
        return "";
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
        if (root.ValueKind != JsonValueKind.Object) return false;
        if (root.TryGetProperty("d", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            payload = d;
            return true;
        }
        payload = root;
        return true;
    }
}
