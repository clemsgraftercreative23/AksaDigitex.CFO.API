using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MyBackend.Infrastructure.Persistence.Entities;

[Table("departments")]
public class DepartmentEntity
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("department_name")]
    [MaxLength(256)]
    public string DepartmentName { get; set; } = string.Empty;

    public ICollection<UserEntity> Users { get; set; } = new List<UserEntity>();
}
