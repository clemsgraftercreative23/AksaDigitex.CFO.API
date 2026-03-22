using System.Security.Claims;
using MyBackend.Application.Services;

namespace MyBackend.Features.Coa;

public static class CoaEndpoints
{
    public static IEndpointRouteBuilder MapCoaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/coa/{no}", async (
                ClaimsPrincipal user,
                string no,
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

                try
                {
                    var rawJson = await service.GetCoaDetailRaw(no, key);
                    return Results.Content(rawJson, "application/json");
                }
                catch (Exception ex)
                {
                    return Results.Json(new { s = false, d = ex.Message }, statusCode: 500);
                }
            })
            .RequireAuthorization()
            .WithTags("COA");

        return app;
    }
}
