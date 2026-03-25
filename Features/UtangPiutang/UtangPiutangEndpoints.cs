using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.UtangPiutang;

/// <summary>
/// Total Piutang / Utang dari Accurate <c>glaccount/detail.do</c> (balance per akun), diagregasi per entitas.
/// </summary>
public static class UtangPiutangEndpoints
{
    /// <summary>Akun piutang (AR / kontrol).</summary>
    public const string CoaPiutangNo = "1103";

    /// <summary>Akun hutang — total = jumlah balance.</summary>
    public static readonly string[] CoaHutangNos = ["2101", "2102", "2103"];

    public static IEndpointRouteBuilder MapUtangPiutangEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/utang-piutang/balances", async (
                ClaimsPrincipal user,
                string[]? company,
                IAccurateService service,
                ICompanyAccessService access,
                CancellationToken cancellationToken) =>
            {
                var companyValues = company ?? Array.Empty<string>();
                var accessResult = await access.NormalizeAndAuthorizeAsync(
                    user,
                    companyValues,
                    cancellationToken);
                if (!accessResult.Success)
                    return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

                var keys = accessResult.AccurateCompanyKeys;
                try
                {
                    var rows = new List<object>(keys.Count);
                    foreach (var key in keys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var piutangRaw = await service.GetCoaDetailRaw(CoaPiutangNo, key);
                        var piutang = TryParseCoaBalance(piutangRaw);

                        var hutangRaw = await Task.WhenAll(
                            CoaHutangNos.Select(no => service.GetCoaDetailRaw(no, key)));
                        var hutang = hutangRaw.Sum(r => TryParseCoaBalance(r));

                        rows.Add(new { company = key, piutang, hutang });
                    }

                    return Results.Json(new { s = true, companies = rows });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
                }
            })
            .RequireAuthorization()
            .WithTags("Utang Piutang");

        app.MapGet("/api/utang-piutang/ar-aging", async (
                ClaimsPrincipal user,
                string asOfDate,
                string[]? company,
                AccurateHttpClient accurateClient,
                ICompanyAccessService access,
                CancellationToken cancellationToken) =>
            {
                if (!DateOnly.TryParse(asOfDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOf))
                    return Results.Json(new { error = "Invalid asOfDate; use yyyy-MM-dd" }, statusCode: 400);

                var companyValues = company ?? Array.Empty<string>();
                var accessResult = await access.NormalizeAndAuthorizeAsync(
                    user,
                    companyValues,
                    cancellationToken);
                if (!accessResult.Success)
                    return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

                var keys = accessResult.AccurateCompanyKeys;
                try
                {
                    var customers = new List<object>();
                    foreach (var key in keys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var rows = await ArAgingComputation.ComputeForCompany(
                            key,
                            asOf,
                            accurateClient,
                            cancellationToken);
                        foreach (var r in rows)
                        {
                            customers.Add(new
                            {
                                company = r.Company,
                                customerId = r.CustomerId,
                                customerName = r.CustomerName,
                                outstanding = r.Outstanding,
                                bucket0_30 = r.Bucket0_30,
                                bucket31_60 = r.Bucket31_60,
                                bucket61_90 = r.Bucket61_90,
                                bucketOver90 = r.BucketOver90,
                                status = r.Status,
                            });
                        }
                    }

                    return Results.Json(new
                    {
                        s = true,
                        asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        companyScope = keys,
                        customers,
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
                }
            })
            .RequireAuthorization()
            .WithTags("Utang Piutang");

        return app;
    }

    private static decimal TryParseCoaBalance(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return 0;

            if (!doc.RootElement.TryGetProperty("d", out var d))
                return 0;
            if (d.ValueKind != JsonValueKind.Object)
                return 0;
            if (!d.TryGetProperty("balance", out var b))
                return 0;

            return b.ValueKind switch
            {
                JsonValueKind.Number => b.GetDecimal(),
                JsonValueKind.String when decimal.TryParse(b.GetString(), out var p) => p,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }
}
