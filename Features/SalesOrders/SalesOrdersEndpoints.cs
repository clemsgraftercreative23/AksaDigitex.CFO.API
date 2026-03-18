using MyBackend.Infrastructure.Clients;

namespace MyBackend.Features.SalesOrders;

public static class SalesOrdersEndpoints
{
    public static IEndpointRouteBuilder MapSalesOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        // Endpoint API untuk halaman Sales Order
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
            })
            .WithTags("Laporan Keuangan");

        return app;
    }
}

