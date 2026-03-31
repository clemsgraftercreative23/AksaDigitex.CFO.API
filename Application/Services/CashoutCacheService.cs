using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MyBackend.Infrastructure.Persistence;

namespace MyBackend.Application.Services;

public sealed record CashoutAggregateDay(DateOnly Date, decimal TotalAmount, int RecordCount, DateTime CachedAtUtc);
public sealed record CashoutAggregateRangeResult(
    bool IsComplete,
    bool IsRunning,
    Dictionary<string, decimal> MonthlyTotals,
    decimal TotalAmount,
    int RecordCount,
    string? Message);

public interface ICashoutCacheService
{
    Task<CashoutAggregateRangeResult> GetRangeAsync(
        IReadOnlyList<string> companies,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken);

    Task EnqueueSyncAsync(
        IReadOnlyList<string> companies,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken);
}

public interface ICashoutSyncQueue
{
    ValueTask EnqueueAsync(CashoutSyncJob job, CancellationToken cancellationToken);
    ValueTask<CashoutSyncJob> DequeueAsync(CancellationToken cancellationToken);
}

public sealed record CashoutSyncJob(
    IReadOnlyList<string> Companies,
    DateOnly FromDate,
    DateOnly ToDate);

public sealed class CashoutSyncQueue : ICashoutSyncQueue
{
    private readonly System.Threading.Channels.Channel<CashoutSyncJob> _channel =
        System.Threading.Channels.Channel.CreateUnbounded<CashoutSyncJob>();

    public ValueTask EnqueueAsync(CashoutSyncJob job, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(job, cancellationToken);

    public ValueTask<CashoutSyncJob> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAsync(cancellationToken);
}

public sealed class CashoutCacheService : ICashoutCacheService
{
    private const string Source = "other-payment";
    private readonly CfoDbContext _db;
    private readonly ICashoutSyncQueue _queue;

    public CashoutCacheService(CfoDbContext db, ICashoutSyncQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<CashoutAggregateRangeResult> GetRangeAsync(
        IReadOnlyList<string> companies,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        var monthly = new Dictionary<string, decimal>(StringComparer.Ordinal);
        decimal total = 0m;
        var recordCount = 0;
        var complete = true;
        var running = false;

        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        foreach (var company in companies)
        {
            await using var statusCmd = conn.CreateCommand();
            statusCmd.CommandText = """
                SELECT status, last_sync_from, last_sync_to
                FROM accurate_sync_status
                WHERE company = @company
                """;
            AddParam(statusCmd, "@company", company);
            await using var statusReader = await statusCmd.ExecuteReaderAsync(cancellationToken);
            if (await statusReader.ReadAsync(cancellationToken))
            {
                var st = statusReader.IsDBNull(0) ? "" : statusReader.GetString(0);
                if (string.Equals(st, "running", StringComparison.OrdinalIgnoreCase))
                    running = true;
            }
            await statusReader.CloseAsync();

            for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    SELECT total_amount, record_count, cached_at, is_partial
                    FROM accurate_cashout_cache
                    WHERE company = @company
                      AND period_date = @periodDate
                      AND source = @source
                    """;
                AddParam(cmd, "@company", company);
                AddParam(cmd, "@periodDate", d);
                AddParam(cmd, "@source", Source);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    complete = false;
                    await reader.CloseAsync();
                    continue;
                }

                var amount = reader.IsDBNull(0) ? 0m : reader.GetDecimal(0);
                var count = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var cachedAt = reader.IsDBNull(2) ? DateTime.MinValue : reader.GetDateTime(2);
                var isPartial = !reader.IsDBNull(3) && reader.GetBoolean(3);
                await reader.CloseAsync();

                if (isPartial || IsExpired(d, cachedAt))
                {
                    complete = false;
                    continue;
                }

                var monthKey = d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                monthly[monthKey] = monthly.TryGetValue(monthKey, out var prev) ? prev + amount : amount;
                total += amount;
                recordCount += count;
            }
        }

        return new CashoutAggregateRangeResult(
            IsComplete: complete,
            IsRunning: running,
            MonthlyTotals: monthly,
            TotalAmount: total,
            RecordCount: recordCount,
            Message: complete ? null : (running ? "Sync cashout sedang berjalan" : "Cache belum lengkap"));
    }

    public async Task EnqueueSyncAsync(
        IReadOnlyList<string> companies,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken)
    {
        await _queue.EnqueueAsync(new CashoutSyncJob(companies, fromDate, toDate), cancellationToken);
    }

    private static bool IsExpired(DateOnly periodDate, DateTime cachedAtUtc)
    {
        if (cachedAtUtc == DateTime.MinValue)
            return true;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var age = now - DateTime.SpecifyKind(cachedAtUtc, DateTimeKind.Utc);

        if (periodDate == today)
            return age > TimeSpan.FromMinutes(5);
        if (periodDate == today.AddDays(-1))
            return age > TimeSpan.FromHours(1);

        var startOfCurrentYear = new DateOnly(today.Year, 1, 1);
        if (periodDate < startOfCurrentYear)
            return false; // tahun lalu: practically immutable

        return age > TimeSpan.FromHours(24);
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
