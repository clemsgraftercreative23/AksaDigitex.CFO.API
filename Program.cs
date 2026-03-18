
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<AccurateHttpClient>();
builder.Services.AddScoped<IAccurateService, AccurateService>();

var app = builder.Build();

app.UseCors("AllowAll");

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "API Running");

app.MapGet("/api/companies", (IAccurateService service) =>
{
    var names = service.GetCompanyNames();
    return Results.Json(names);
})
.WithName("GetCompanies")
.WithTags("Companies");

app.MapGet("/api/database-host", async (string? company, IAccurateService service) =>
{
    return await service.GetDatabaseHost(company);
}).WithTags("Companies");

// Return raw JSON from Accurate so envelope { "s", "d" } and "balance" are preserved (no double-serialize).
// Optional query: ?company=PT%20WONG%20HANG%20BERSAUDARA (nama PT persis seperti di /api/companies)
app.MapGet("/api/coa/{no}", async (string no, string? company, IAccurateService service) =>
{
    try
    {
        var rawJson = await service.GetCoaDetailRaw(no, company);
        return Results.Content(rawJson, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
    }
}).WithTags("COA");

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
                var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var d = doc.RootElement.TryGetProperty("d", out var prop) ? prop : default;
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


// Endpoint API buatanmu untuk halaman Sales Order
app.MapGet("/api/sales-orders", async (AccurateHttpClient accurateClient, string? company) =>
{
    try
    {
        var result = await accurateClient.GetSalesOrdersRaw(company);
        return Results.Content(result, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
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
                var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var d = doc.RootElement.TryGetProperty("d", out var prop) ? prop : default;
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
});

app.Run();
