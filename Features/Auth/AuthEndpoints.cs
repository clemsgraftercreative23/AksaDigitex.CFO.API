using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MyBackend.Application.Auth;
using MyBackend.Application.Services;
using MyBackend.Features.Auth;

namespace MyBackend.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async Task<IResult> (
                [FromBody] LoginRequest request,
                IAuthService auth,
                CancellationToken cancellationToken) =>
            {
                var result = await auth.LoginAsync(request, cancellationToken);
                if (result == null)
                    return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);
                return Results.Json(result);
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithTags("Auth");

        // Stateless JWT: logout hanya mengakhiri sesi di sisi klien. Endpoint ini tetap ada agar klien bisa memanggil hook konsisten.
        app.MapPost("/api/auth/logout", (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.NoContent() : Results.Unauthorized())
            .RequireAuthorization()
            .WithTags("Auth");

        app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
            {
                if (user.Identity?.IsAuthenticated != true)
                    return Results.Unauthorized();
                if (!int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
                    return Results.Unauthorized();

                var email = user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
                var name = user.FindFirstValue(JwtRegisteredClaimNames.Name)
                    ?? user.FindFirstValue(ClaimTypes.Name)
                    ?? string.Empty;
                var role = user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
                _ = int.TryParse(user.FindFirst(AppClaims.CompanyId)?.Value, out var companyId);
                var isAll = string.Equals(user.FindFirst(AppClaims.IsAllCompany)?.Value, "true", StringComparison.OrdinalIgnoreCase);

                var dto = new UserSummaryDto
                {
                    Id = id,
                    Email = email,
                    FullName = name,
                    Role = role,
                    CompanyId = companyId == 0 ? null : companyId,
                    IsAllCompany = isAll,
                };
                return Results.Json(dto);
            })
            .RequireAuthorization()
            .WithTags("Auth");

        return app;
    }
}
