using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyBackend.Application.Options;
using MyBackend.Features.Auth;
using MyBackend.Infrastructure.Persistence;
using MyBackend.Infrastructure.Security;

namespace MyBackend.Application.Services;

public class AuthService : IAuthService
{
    private readonly CfoDbContext _db;
    private readonly IPasswordHasherService _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly JwtOptions _jwt;

    public AuthService(
        CfoDbContext db,
        IPasswordHasherService passwordHasher,
        ITokenService tokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _jwt = jwtOptions.Value;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrEmpty(email))
            return null;

        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);

        if (user == null || !user.IsActive || user.Role == null)
            return null;

        if (!_passwordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            return null;

        user.LastActiveAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var access = _tokenService.CreateAccessToken(user, user.Role.RoleName);

        return new LoginResponse
        {
            AccessToken = access,
            ExpiresInSeconds = _jwt.AccessTokenMinutes * 60,
            TokenType = "Bearer",
            User = UserSummaryDto.FromEntity(user, user.Role.RoleName),
        };
    }
}
