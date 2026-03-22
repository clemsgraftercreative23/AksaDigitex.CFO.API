using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyBackend.Infrastructure.Persistence.Entities;

[Table("users")]
public class UserEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("full_name")]
    [MaxLength(512)]
    public string FullName { get; set; } = string.Empty;

    [Column("email")]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Column("password_hash")]
    [MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("role_id")]
    public int RoleId { get; set; }

    [ForeignKey(nameof(RoleId))]
    public RoleEntity? Role { get; set; }

    [Column("position")]
    [MaxLength(256)]
    public string? Position { get; set; }

    [Column("company_id")]
    public int? CompanyId { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public CompanyEntity? Company { get; set; }

    [Column("department_id")]
    public int? DepartmentId { get; set; }

    [ForeignKey(nameof(DepartmentId))]
    public DepartmentEntity? Department { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("mfa_secret")]
    [MaxLength(512)]
    public string? MfaSecret { get; set; }

    [Column("mfa_enabled")]
    public bool MfaEnabled { get; set; }

    [Column("last_mfa_verified_at")]
    public DateTime? LastMfaVerifiedAt { get; set; }

    [Column("last_active_at")]
    public DateTime? LastActiveAt { get; set; }

    [Column("notif_threshold_min")]
    public int? NotifThresholdMin { get; set; }

    [Column("notif_threshold_max")]
    public int? NotifThresholdMax { get; set; }

    [Column("urgency_email")]
    [MaxLength(256)]
    public string? UrgencyEmail { get; set; }

    [Column("enable_urgensi")]
    public bool? EnableUrgensi { get; set; }

    [Column("fcm_token")]
    [MaxLength(512)]
    public string? FcmToken { get; set; }

    [Column("is_all_company")]
    public bool IsAllCompany { get; set; }
}
