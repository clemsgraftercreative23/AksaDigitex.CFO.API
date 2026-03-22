using System.Security.Claims;
using MyBackend.Application.Services;

namespace MyBackend.Features.Companies;

public static class CompaniesEndpoints
{
    public static IEndpointRouteBuilder MapCompaniesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/companies", async (
                ClaimsPrincipal user,
                IAccurateService service,
                ICompanyAccessService access,
                CancellationToken cancellationToken) =>
            {
                var allowed = await access.GetAllowedAccurateCompanyKeysAsync(user, cancellationToken);
                var all = service.GetCompanyNames();
                var filtered = all
                    .Where(a => allowed.Any(x => string.Equals(x, a, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                return Results.Json(filtered);
            })
            .RequireAuthorization()
            .WithName("GetCompanies")
            .WithTags("Companies");

        app.MapGet("/api/database-host", async (
                ClaimsPrincipal user,
                string? company,
                IAccurateService service,
                ICompanyAccessService access,
                CancellationToken cancellationToken) =>
            {
                var accessResult = await access.NormalizeAndAuthorizeAsync(
                    user,
                    company != null ? new[] { company } : Array.Empty<string?>(),
                    cancellationToken);
                if (!accessResult.Success)
                    return Results.Json(new { error = accessResult.Error }, statusCode: accessResult.StatusCode);
                var key = accessResult.AccurateCompanyKeys[0];
                return await service.GetDatabaseHost(key);
            })
            .RequireAuthorization()
            .WithTags("Companies");

        return app;
    }
}
