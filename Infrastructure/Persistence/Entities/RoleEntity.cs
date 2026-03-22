using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyBackend.Infrastructure.Persistence.Entities;

[Table("roles")]
public class RoleEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("role_name")]
    [MaxLength(256)]
    public string RoleName { get; set; } = string.Empty;

    public ICollection<UserEntity> Users { get; set; } = new List<UserEntity>();
}
