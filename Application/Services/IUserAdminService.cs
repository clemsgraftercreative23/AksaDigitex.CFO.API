using System.Security.Claims;
using MyBackend.Features.Users;

namespace MyBackend.Application.Services;

public interface IUserAdminService
{
    Task<IReadOnlyList<UserListItemDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<UserDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<(bool ok, int statusCode, string? error, UserDetailDto? user)> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<(bool ok, int statusCode, string? error, UserDetailDto? user)> UpdateAsync(int id, UpdateUserRequest request, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
    Task<(bool ok, int statusCode, string? error)> SoftDeleteAsync(int id, ClaimsPrincipal actor, CancellationToken cancellationToken = default);
}
