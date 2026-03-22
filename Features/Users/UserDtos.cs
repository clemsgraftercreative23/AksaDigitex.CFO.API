namespace MyBackend.Features.Users;

public class UserListItemDto
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; }
    public bool IsAllCompany { get; set; }
}

public class UserDetailDto : UserListItemDto
{
    public string? Position { get; set; }
    public string? CompanyName { get; set; }
    public string? DepartmentName { get; set; }
}

public class CreateUserRequest
{
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
    public string? RoleName { get; set; }
    public string? Position { get; set; }
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsAllCompany { get; set; }
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }
    public string? RoleName { get; set; }
    public string? Position { get; set; }
    public int? CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsAllCompany { get; set; }
    /// <summary>When set, updates password.</summary>
    public string? Password { get; set; }
}
