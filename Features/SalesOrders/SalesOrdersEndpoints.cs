using System.Security.Claims;
using MyBackend.Application.Services;
using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.SalesOrders;

public static class SalesOrdersEndpoints
{
    public static IEndpointRouteBuilder MapSalesOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sales-orders", async (
                ClaimsPrincipal user,
                string? company,
                AccurateHttpClient accurateClient,
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
                    var result = await accurateClient.GetSalesOrdersRaw(key);
                    return Results.Content(result, "application/json");
                }
                catch (Exception ex)
                {
                    return Results.Problem(ex.Message);
                }
            })
            .RequireAuthorization()
            .WithTags("Laporan Keuangan");

        return app;
    }
}
