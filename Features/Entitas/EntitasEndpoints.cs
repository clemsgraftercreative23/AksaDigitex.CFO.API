using Microsoft.EntityFrameworkCore;
using MyBackend.Application.Auth;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Persistence;
using MyBackend.Infrastructure.Persistence.Entities;

namespace MyBackend.Features.Entitas;

public static class EntitasEndpoints
{
    private const string DefaultCurrency = "IDR";
    private const string DefaultSourceData = "Accurate Online";

    public static IEndpointRouteBuilder MapEntitasEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/entitas", async (
                CfoDbContext db,
                IAccurateService accurate,
                CancellationToken cancellationToken) =>
            {
                // UI ini untuk admin.
                // Route ini tetap diberi authorization lewat policy di bawah.

                var accurateCompanyKeys = accurate.GetCompanyNames();
                var dbCompanies = await db.Companies.AsNoTracking().ToListAsync(cancellationToken);

                var now = DateTimeOffset.UtcNow;
                var semaphore = new SemaphoreSlim(6);

                async Task<EntitasRowDto> computeRow(string companyKey)
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        CompanyEntity? match = dbCompanies.FirstOrDefault(c =>
                            (!string.IsNullOrWhiteSpace(c.CompanyName) && string.Equals(c.CompanyName, companyKey, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrWhiteSpace(c.CompanyCode) && string.Equals(c.CompanyCode, companyKey, StringComparison.OrdinalIgnoreCase)));

                        var companyName = match?.CompanyName ?? companyKey;
                        var companyCode = match?.CompanyCode;
                        var wilayah = GetWilayah(match?.Address);
                        // Di requirement terbaru, seluruh entitas berasal dari Accurate.
                        var sumberData = DefaultSourceData;

                        try
                        {
                            // Probe koneksi/auth ringan via endpoint Accurate token.
                            // Kalau sukses => Terhubung, kalau gagal => Error/Gagal Auth.
                            await accurate.GetDatabaseHost(companyKey);

                            return new EntitasRowDto(
                                companyKey: companyKey,
                                companyName: companyName,
                                companyCode: companyCode,
                                wilayah: wilayah,
                                mataUang: DefaultCurrency,
                                sumberData: sumberData,
                                statusKoneksi: "Terhubung",
                                lastSyncAtUtc: now,
                                errorMessage: null);
                        }
                        catch (Exception ex)
                        {
                            var msg = ex.Message ?? string.Empty;
                            var status = IsAuthError(msg) ? "Gagal Auth" : "Error";
                            var errShort = Truncate(msg, 180);

                            return new EntitasRowDto(
                                companyKey: companyKey,
                                companyName: companyName,
                                companyCode: companyCode,
                                wilayah: wilayah,
                                mataUang: DefaultCurrency,
                                sumberData: sumberData,
                                statusKoneksi: status,
                                lastSyncAtUtc: now,
                                errorMessage: errShort);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }

                var tasks = accurateCompanyKeys.Select(computeRow).ToArray();
                var rows = await Task.WhenAll(tasks);
                return Results.Json(rows);
            })
            .RequireAuthorization(AuthConstants.SuperDuperAdminPolicy)
            .WithTags("Entitas");

        return app;
    }

    private static bool IsAuthError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        var m = message.ToLowerInvariant();
        return m.Contains("401") ||
               m.Contains("unauthorized") ||
               m.Contains("forbidden") ||
               m.Contains("unauth");
    }

    private static string GetWilayah(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return "-";

        // Best-effort: ambil segmen pertama sebelum koma/semicolon.
        var s = address.Trim();
        var cut = s.IndexOfAny(new[] { ',', ';' });
        if (cut >= 0)
            s = s[..cut].Trim();
        return string.IsNullOrWhiteSpace(s) ? "-" : s;
    }

    private static string Truncate(string s, int maxLen)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Length <= maxLen ? s : s[..maxLen];
    }

    public sealed record EntitasRowDto(
        string companyKey,
        string companyName,
        string? companyCode,
        string wilayah,
        string mataUang,
        string sumberData,
        string statusKoneksi,
        DateTimeOffset lastSyncAtUtc,
        string? errorMessage);
}

