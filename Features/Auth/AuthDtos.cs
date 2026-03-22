using MyBackend.Infrastructure.Persistence.Entities;

namespace MyBackend.Features.Auth;

public class LoginRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
}

public class LoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
    public string TokenType { get; set; } = "Bearer";
    public UserSummaryDto User { get; set; } = null!;
}

public class UserSummaryDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public bool IsAllCompany { get; set; }

    public static UserSummaryDto FromEntity(UserEntity user, string roleName) =>
        new()
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = roleName,
            CompanyId = user.CompanyId,
            IsAllCompany = user.IsAllCompany,
        };
}
