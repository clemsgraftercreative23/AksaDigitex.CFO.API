using System.Security.Claims;
using System.Text.Json;
using MyBackend.Application.Services;

namespace MyBackend.Features.LaporanKeuangan;

public static class LaporanKeuanganEndpoints
{
    public static IEndpointRouteBuilder MapLaporanKeuanganEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/laporan-keuangan/laba-rugi", async (
            ClaimsPrincipal user,
            string fromDate,
            string toDate,
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
                if (keys.Count >= 2)
                {
                    var companiesList = new List<object>();
                    foreach (var companyName in keys)
                    {
                        var rawJson = await service.GetPlAccountAmountRaw(fromDate, toDate, companyName);
                        using var doc = JsonDocument.Parse(rawJson);
                        var d = doc.RootElement.TryGetProperty("d", out var prop)
                            ? prop.Clone()
                            : JsonDocument.Parse("null").RootElement.Clone();
                        companiesList.Add(new { companyName, data = d });
                    }

                    return Results.Json(new { s = true, companies = companiesList });
                }

                var singleCompany = keys.Count == 1 ? keys[0] : null;
                var json = await service.GetPlAccountAmountRaw(fromDate, toDate, singleCompany);
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        app.MapGet("/api/laporan-keuangan/neraca", async (
            ClaimsPrincipal user,
            string asOfDate,
            HttpRequest request,
            IAccurateService service,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var companyValues = request.Query["company"].ToArray();
            var accessResult = await access.NormalizeAndAuthorizeAsync(
                user,
                companyValues,
                cancellationToken);
            if (!accessResult.Success)
                return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);

            var keys = accessResult.AccurateCompanyKeys;
            try
            {
                if (keys.Count >= 2)
                {
                    var companiesList = new List<object>();
                    foreach (var companyName in keys)
                    {
                        var rawJson = await service.GetBsAccountAmountRaw(asOfDate, companyName);
                        using var doc = JsonDocument.Parse(rawJson);
                        var d = doc.RootElement.TryGetProperty("d", out var prop)
                            ? prop.Clone()
                            : JsonDocument.Parse("null").RootElement.Clone();
                        companiesList.Add(new { companyName, data = d });
                    }

                    return Results.Json(new { s = true, companies = companiesList });
                }

                var singleCompany = keys.Count == 1 ? keys[0] : null;
                var json = await service.GetBsAccountAmountRaw(asOfDate, singleCompany);
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        app.MapGet("/api/reports/summary", async (
            ClaimsPrincipal user,
            ICompanyAccessService access,
            CancellationToken cancellationToken) =>
        {
            var allowed = await access.GetAllowedAccurateCompanyKeysAsync(user, cancellationToken);
            return Results.Json(new
            {
                message = "Contoh endpoint laporan berbasis hak akses perusahaan.",
                allowedAccurateCompanyKeys = allowed,
            });
        })
            .RequireAuthorization()
            .WithTags("Reports");

        return app;
    }
}
