using System.Security.Claims;
using MyBackend.Application.Services;

namespace MyBackend.Features.PurchaseOrders;

public static class PurchaseOrdersEndpoints
{
    public static IEndpointRouteBuilder MapPurchaseOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/purchase-orders", async (
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
                    var rows = new List<object>();
                    foreach (var key in keys)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var companyRows = await PurchaseOrdersComputation.ComputeForCompany(
                            key,
                            service,
                            cancellationToken);
                        rows.AddRange(companyRows.Select(r => new
                        {
                            company = r.Company,
                            id = r.Id,
                            number = r.Number,
                            shipDate = r.ShipDate,
                            billNumber = r.BillNumber,
                            name = r.Name,
                            description = r.Description,
                            statusName = r.StatusName,
                            totalAmount = r.TotalAmount,
                        }));
                    }

                    return Results.Json(new
                    {
                        s = true,
                        companyScope = keys,
                        rows,
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
            .WithTags("Purchase Orders");

        return app;
    }
}
