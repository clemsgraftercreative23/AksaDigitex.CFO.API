using System.Text.Json;
using MyBackend.Application.Services;

namespace MyBackend.Features.LaporanKeuangan;

public static class LaporanKeuanganEndpoints
{
    public static IEndpointRouteBuilder MapLaporanKeuanganEndpoints(this IEndpointRouteBuilder app)
    {
        // Laporan Keuangan - Laba Rugi (P&L). fromDate/toDate in dd/MM/yyyy.
        // Single: ?company=PT Name → returns { s, d: array }.
        // Multi:  ?company=PT1&company=PT2 → returns { s, companies: [{ companyName, data }] }.
        app.MapGet("/api/laporan-keuangan/laba-rugi", async (
            string fromDate,
            string toDate,
            string[]? company,
            IAccurateService service) =>
        {
            var companyValues = company ?? Array.Empty<string>();
            try
            {
                if (companyValues.Length >= 2)
                {
                    var companiesList = new List<object>();
                    foreach (var companyName in companyValues)
                    {
                        if (string.IsNullOrWhiteSpace(companyName)) continue;
                        var rawJson = await service.GetPlAccountAmountRaw(fromDate, toDate, companyName);
                        using var doc = JsonDocument.Parse(rawJson);
                        // Clone JsonElement so it can outlive JsonDocument disposal (prevents ObjectDisposedException).
                        var d = doc.RootElement.TryGetProperty("d", out var prop)
                            ? prop.Clone()
                            : JsonDocument.Parse("null").RootElement.Clone();
                        companiesList.Add(new { companyName = companyName.Trim(), data = d });
                    }

                    return Results.Json(new { s = true, companies = companiesList });
                }

                var singleCompany = companyValues.Length == 1 ? companyValues[0] : null;
                var json = await service.GetPlAccountAmountRaw(fromDate, toDate, singleCompany);
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        }).WithTags("Laporan Keuangan");

        // Laporan Keuangan - Neraca (Balance Sheet). asOfDate in dd/MM/yyyy.
        // Single: ?company=PT Name → returns { s, d: array }.
        // Multi:  ?company=PT1&company=PT2 → returns { s, companies: [{ companyName, data }] }.
        app.MapGet("/api/laporan-keuangan/neraca", async (
            string asOfDate,
            HttpRequest request,
            IAccurateService service) =>
        {
            var companyValues = request.Query["company"].ToArray();
            try
            {
                if (companyValues.Length >= 2)
                {
                    var companiesList = new List<object>();
                    foreach (var companyName in companyValues)
                    {
                        if (string.IsNullOrWhiteSpace(companyName)) continue;
                        var rawJson = await service.GetBsAccountAmountRaw(asOfDate, companyName);
                        using var doc = JsonDocument.Parse(rawJson);
                        // Clone JsonElement so it can outlive JsonDocument disposal (prevents ObjectDisposedException).
                        var d = doc.RootElement.TryGetProperty("d", out var prop)
                            ? prop.Clone()
                            : JsonDocument.Parse("null").RootElement.Clone();
                        companiesList.Add(new { companyName = companyName.Trim(), data = d });
                    }

                    return Results.Json(new { s = true, companies = companiesList });
                }

                var singleCompany = companyValues.Length == 1 ? companyValues[0] : null;
                var json = await service.GetBsAccountAmountRaw(asOfDate, singleCompany);
                return Results.Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
            }
        }).WithTags("Laporan Keuangan");

        return app;
    }
}

