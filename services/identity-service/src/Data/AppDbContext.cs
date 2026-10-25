using IdentityService.Entities;
using IdentityService.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

// IDataProtectionKeyContext: lưu khóa Data Protection vào DB để token xác thực email / đặt lại mật khẩu
// vẫn hợp lệ sau khi container khởi động lại hoặc khi chạy nhiều instance.
public class AppDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Department> Departments => Set<Department>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
            e.Property(x => x.ReplacedByTokenHash).HasMaxLength(128);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);

            e.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Permission>(e =>
        {
            e.ToTable("Permissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(100);
            e.Property(x => x.Description).IsRequired().HasMaxLength(256);
            e.Property(x => x.Module).IsRequired().HasMaxLength(64);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Module);
        });

        builder.Entity<Department>(e =>
        {
            e.ToTable("Departments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired().HasMaxLength(50);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasIndex(x => x.Code).IsUnique();
        });
    }
}
