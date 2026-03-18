using MyBackend.Application.Services;

namespace MyBackend.Features.Companies;

public static class CompaniesEndpoints
{
    public static IEndpointRouteBuilder MapCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/companies", (IAccurateService service) =>
            {
                var names = service.GetCompanyNames();
                return Results.Json(names);
            })
            .WithName("GetCompanies")
            .WithTags("Companies");

        app.MapGet("/api/database-host", async (string? company, IAccurateService service) =>
            await service.GetDatabaseHost(company))
            .WithTags("Companies");

        return app;
    }
}

