using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyBackend.Infrastructure.Persistence.Entities;

[Table("companies")]
public class CompanyEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_name")]
    [MaxLength(512)]
    public string CompanyName { get; set; } = string.Empty;

    [Column("company_code")]
    [MaxLength(128)]
    public string? CompanyCode { get; set; }

    [Column("address")]
    public string? Address { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    public ICollection<UserEntity> Users { get; set; } = new List<UserEntity>();
}
