using Microsoft.EntityFrameworkCore;
using MyBackend.Application.Auth;
using MyBackend.Infrastructure.Persistence;

namespace MyBackend.Features.Lookup;

public static class LookupEndpoints
{
    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/lookup")
            .RequireAuthorization(AuthConstants.SuperDuperAdminPolicy)
            .WithTags("Lookup");

        group.MapGet("/companies", async (CfoDbContext db, CancellationToken cancellationToken) =>
        {
            var rows = await db.Companies.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.CompanyName)
                .Select(c => new CompanyLookupDto(c.Id, c.CompanyName, c.CompanyCode))
                .ToListAsync(cancellationToken);
            return Results.Json(rows);
        }).WithName("LookupCompanies");

        group.MapGet("/departments", async (CfoDbContext db, CancellationToken cancellationToken) =>
        {
            var rows = await db.Departments.AsNoTracking()
                .OrderBy(d => d.DepartmentName)
                .Select(d => new DepartmentLookupDto(d.Id, d.DepartmentName))
                .ToListAsync(cancellationToken);
            return Results.Json(rows);
        }).WithName("LookupDepartments");

        group.MapGet("/roles", async (CfoDbContext db, CancellationToken cancellationToken) =>
        {
            var rows = await db.Roles.AsNoTracking()
                .OrderBy(r => r.RoleName)
                .Select(r => new RoleLookupDto(r.Id, r.RoleName))
                .ToListAsync(cancellationToken);
            return Results.Json(rows);
        }).WithName("LookupRoles");

        return app;
    }

    private sealed record CompanyLookupDto(int Id, string CompanyName, string? CompanyCode);

    private sealed record DepartmentLookupDto(int Id, string DepartmentName);

    private sealed record RoleLookupDto(int Id, string RoleName);
}
