using MyBackend.Application.Services;

namespace MyBackend.Features.Coa;

public static class CoaEndpoints
{
    public static IEndpointRouteBuilder MapCoaEndpoints(this IEndpointRouteBuilder app)
    {
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
            })
            .WithTags("COA");

        return app;
    }
}

