using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyBackend.Application.Auth;
using MyBackend.Features.Users;
using MyBackend.Infrastructure.Persistence;
using MyBackend.Infrastructure.Persistence.Entities;
using MyBackend.Infrastructure.Security;

namespace MyBackend.Application.Services;

public class UserAdminService : IUserAdminService
{
    private readonly CfoDbContext _db;
    private readonly IPasswordHasherService _passwordHasher;

    public UserAdminService(CfoDbContext db, IPasswordHasherService passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(
        int? companyId = null,
        int? departmentId = null,
        bool? isActive = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .AsQueryable();

        if (companyId.HasValue)
            query = query.Where(u => u.CompanyId == companyId.Value);

        if (departmentId.HasValue)
            query = query.Where(u => u.DepartmentId == departmentId.Value);

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.Email.ToLower().Contains(term) ||
                u.FullName.ToLower().Contains(term));
        }

        return await query
            .OrderBy(u => u.Id)
            .Select(u => new UserListItemDto
            {
                Id = u.Id,
                Email = u.Email,
                FullName = u.FullName,
                RoleName = u.Role!.RoleName,
                CompanyId = u.CompanyId,
                DepartmentId = u.DepartmentId,
                IsActive = u.IsActive,
                IsAllCompany = u.IsAllCompany,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<UserDetailDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var u = await _db.Users
            .AsNoTracking()
            .Include(x => x.Role)
            .Include(x => x.Company)
            .Include(x => x.Department)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return u == null ? null : MapDetail(u);
    }

    public async Task<(bool ok, int statusCode, string? error, UserDetailDto? user)> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return (false, 400, "Email is required.", null);
        if (string.IsNullOrWhiteSpace(request.Password))
            return (false, 400, "Password is required.", null);
        if (string.IsNullOrWhiteSpace(request.FullName))
            return (false, 400, "Full name is required.", null);
        if (string.IsNullOrWhiteSpace(request.RoleName))
            return (false, 400, "Role name is required.", null);

        var exists = await _db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
        if (exists)
            return (false, 409, "Email already exists.", null);

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName.ToLower() == request.RoleName!.Trim().ToLower(), cancellationToken);
        if (role == null)
            return (false, 400, "Unknown role.", null);

        // Required fields rule for user admin:
        // - DepartmentId must always be set.
        // - CompanyId must be set when IsAllCompany is false.
        if (request.DepartmentId == null)
            return (false, 400, "Department is required.", null);

        if (!request.IsAllCompany && request.CompanyId == null)
            return (false, 400, "Company is required.", null);

        if (request.CompanyId is { } cid)
        {
            var companyOk = await _db.Companies.AnyAsync(c => c.Id == cid, cancellationToken);
            if (!companyOk)
                return (false, 400, "Unknown company id.", null);
        }

        if (request.DepartmentId is { } did)
        {
            var deptOk = await _db.Departments.AnyAsync(d => d.Id == did, cancellationToken);
            if (!deptOk)
                return (false, 400, "Unknown department id.", null);
        }

        var entity = new UserEntity
        {
            Email = email,
            FullName = request.FullName!.Trim(),
            PasswordHash = _passwordHasher.HashPassword(request.Password!),
            RoleId = role.Id,
            Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim(),
            CompanyId = request.CompanyId,
            DepartmentId = request.DepartmentId,
            IsActive = request.IsActive,
            IsAllCompany = request.IsAllCompany,
            CreatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        var created = await GetByIdAsync(entity.Id, cancellationToken);
        return (true, 201, null, created);
    }

    public async Task<(bool ok, int statusCode, string? error, UserDetailDto? user)> UpdateAsync(
        int id,
        UpdateUserRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
            return (false, 404, "User not found.", null);

        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleName.ToLower() == request.RoleName!.Trim().ToLower(), cancellationToken);
            if (role == null)
                return (false, 400, "Unknown role.", null);
            if (IsSelf(actor, id) && !string.Equals(role.RoleName, AuthConstants.SuperDuperAdminRole, StringComparison.OrdinalIgnoreCase)
                                   && string.Equals(user.Role?.RoleName, AuthConstants.SuperDuperAdminRole, StringComparison.OrdinalIgnoreCase))
                return (false, 400, "You cannot remove your own super_duper_admin role.", null);
            user.RoleId = role.Id;
        }

        if (request.FullName != null)
            user.FullName = request.FullName.Trim();
        if (request.Position != null)
            user.Position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim();

        // Enforce required fields on update:
        // - DepartmentId must always be set (either supplied in request or already present on user).
        // - CompanyId must be set when effective IsAllCompany is false.
        var effectiveIsAllCompany = request.IsAllCompany ?? user.IsAllCompany;

        if (request.DepartmentId.HasValue)
        {
            var did = request.DepartmentId.Value;
            var deptOk = await _db.Departments.AnyAsync(d => d.Id == did, cancellationToken);
            if (!deptOk)
                return (false, 400, "Unknown department id.", null);
            user.DepartmentId = did;
        }
        else if (user.DepartmentId == null)
        {
            return (false, 400, "Department is required.", null);
        }

        if (effectiveIsAllCompany == false)
        {
            if (request.CompanyId.HasValue)
            {
                var cid = request.CompanyId.Value;
                var companyOk = await _db.Companies.AnyAsync(c => c.Id == cid, cancellationToken);
                if (!companyOk)
                    return (false, 400, "Unknown company id.", null);
                user.CompanyId = cid;
            }
            else if (user.CompanyId == null)
            {
                return (false, 400, "Company is required.", null);
            }
        }
        else
        {
            // Access-all: companyId is optional, but if provided we still validate.
            if (request.CompanyId.HasValue)
            {
                var cid = request.CompanyId.Value;
                var companyOk = await _db.Companies.AnyAsync(c => c.Id == cid, cancellationToken);
                if (!companyOk)
                    return (false, 400, "Unknown company id.", null);
                user.CompanyId = cid;
            }
        }

        if (request.IsActive.HasValue)
        {
            if (IsSelf(actor, id) && request.IsActive == false)
                return (false, 400, "You cannot deactivate your own account.", null);
            user.IsActive = request.IsActive.Value;
        }

        if (request.IsAllCompany.HasValue)
            user.IsAllCompany = request.IsAllCompany.Value;

        if (!string.IsNullOrWhiteSpace(request.Password))
            user.PasswordHash = _passwordHasher.HashPassword(request.Password!);

        await _db.SaveChangesAsync(cancellationToken);
        var dto = await GetByIdAsync(id, cancellationToken);
        return (true, 200, null, dto);
    }

    public async Task<(bool ok, int statusCode, string? error)> SoftDeleteAsync(
        int id,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
            return (false, 404, "User not found.");
        if (IsSelf(actor, id))
            return (false, 400, "You cannot deactivate your own account.");
        user.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        return (true, 204, null);
    }

    private static bool IsSelf(ClaimsPrincipal actor, int userId) =>
        int.TryParse(actor.FindFirstValue(ClaimTypes.NameIdentifier), out var selfId) && selfId == userId;

    private static UserDetailDto MapDetail(UserEntity u) =>
        new()
        {
            Id = u.Id,
            Email = u.Email,
            FullName = u.FullName,
            RoleName = u.Role!.RoleName,
            CompanyId = u.CompanyId,
            DepartmentId = u.DepartmentId,
            IsActive = u.IsActive,
            IsAllCompany = u.IsAllCompany,
            Position = u.Position,
            CompanyName = u.Company?.CompanyName,
            DepartmentName = u.Department?.DepartmentName,
        };
}
