using Microsoft.AspNetCore.Mvc;
using MyBackend.Application.Auth;
using MyBackend.Application.Services;

namespace MyBackend.Features.Users;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/users")
            .RequireAuthorization(AuthConstants.SuperDuperAdminPolicy)
            .WithTags("Users");

        admin.MapGet(
            "/",
            async (
                int? companyId,
                int? departmentId,
                bool? isActive,
                string? search,
                IUserAdminService users,
                CancellationToken cancellationToken) =>
                Results.Json(await users.ListAsync(companyId, departmentId, isActive, search, cancellationToken)));

        admin.MapGet("/{id:int}", async Task<IResult> (int id, IUserAdminService users, CancellationToken cancellationToken) =>
        {
            var u = await users.GetByIdAsync(id, cancellationToken);
            return u == null ? Results.NotFound() : Results.Json(u);
        });

        admin.MapPost("/", async Task<IResult> (
                [FromBody] CreateUserRequest request,
                IUserAdminService users,
                CancellationToken cancellationToken) =>
            {
                var (ok, status, error, user) = await users.CreateAsync(request, cancellationToken);
                if (!ok)
                    return Results.Json(new { error }, statusCode: status);
                return Results.Json(user, statusCode: status);
            });

        admin.MapPatch("/{id:int}", async Task<IResult> (
                int id,
                [FromBody] UpdateUserRequest request,
                IUserAdminService users,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var (ok, status, error, user) = await users.UpdateAsync(id, request, http.User, cancellationToken);
                if (!ok)
                    return Results.Json(new { error }, statusCode: status);
                return Results.Json(user);
            });

        admin.MapDelete("/{id:int}", async Task<IResult> (
                int id,
                IUserAdminService users,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var (ok, status, error) = await users.SoftDeleteAsync(id, http.User, cancellationToken);
                if (!ok)
                    return Results.Json(new { error }, statusCode: status);
                return Results.NoContent();
            });

        admin.MapDelete("/{id:int}/hard", async Task<IResult> (
                int id,
                IUserAdminService users,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var (ok, status, error) = await users.HardDeleteAsync(id, http.User, cancellationToken);
                if (!ok)
                    return Results.Json(new { error }, statusCode: status);
                return Results.NoContent();
            });

        return app;
    }
}
