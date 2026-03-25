using System.IO;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using MyBackend.Application.Options;
using MyBackend.Features.Auth;
using MyBackend.Infrastructure.Persistence;
using MyBackend.Infrastructure.Persistence.Entities;
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

        UserEntity? user;
        try
        {
            user = await _db.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
        }
        catch (NpgsqlException ex) when (IsTransientConnectionClosed(ex))
        {
            // Jika koneksi DB putus paksa (transient), jangan biarkan request jadi 500.
            // Anggap login gagal (endpoint akan balas 401).
            return null;
        }
        catch (Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException ex)
        {
            // EnableRetryOnFailure kita bisa menghasilkan exception wrapper setelah retry habis.
            if (ex.InnerException is NpgsqlException npg && IsTransientConnectionClosed(npg))
                return null;

            throw;
        }

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

    private static bool IsTransientConnectionClosed(Exception ex)
    {
        // kasus seperti: "Unable to read data... forcibly closed by the remote host."
        if (ex.Message.Contains("forcibly closed", StringComparison.OrdinalIgnoreCase))
            return true;

        var se = FindInner<SocketException>(ex);
        if (se != null && se.NativeErrorCode == 10054) // WSAECONNRESET
            return true;

        return FindInner<IOException>(ex) != null;
    }

    private static T? FindInner<T>(Exception ex) where T : Exception
    {
        while (ex != null)
        {
            if (ex is T t) return t;
            ex = ex.InnerException!;
        }

        return default;
    }
}
