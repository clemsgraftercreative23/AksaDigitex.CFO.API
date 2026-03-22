using Microsoft.EntityFrameworkCore;
using MyBackend.Infrastructure.Persistence.Entities;

namespace MyBackend.Infrastructure.Persistence;

public class CfoDbContext : DbContext
{
    public CfoDbContext(DbContextOptions<CfoDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<RoleEntity> Roles => Set<RoleEntity>();
    public DbSet<CompanyEntity> Companies => Set<CompanyEntity>();
    public DbSet<DepartmentEntity> Departments => Set<DepartmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserEntity>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne(x => x.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Company)
                .WithMany(c => c.Users)
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department)
                .WithMany(d => d.Users)
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
