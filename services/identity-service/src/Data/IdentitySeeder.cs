using IdentityService.Entities;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

public static class IdentitySeeder
{
    /// <summary>
    /// Đồng bộ danh mục permission từ code vào DB. Luôn chạy (không phụ thuộc SEED_DATA) vì thiếu danh mục
    /// thì mọi [HasPermission] đều bị từ chối. Chỉ THÊM permission còn thiếu và cập nhật mô tả/nhóm;
    /// KHÔNG ghi đè Level đã có trong DB, và không xóa permission không còn trong code.
    /// </summary>
    public static async Task SyncPermissionsAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Permissions.ToDictionaryAsync(p => p.Code, StringComparer.Ordinal);

        foreach (var def in AppPermissions.Definitions)
        {
            if (existing.TryGetValue(def.Code, out var permission))
            {
                // Chỉ metadata; Level giữ nguyên theo DB.
                permission.Description = def.Description;
                permission.Module = def.Module;
                continue;
            }

            db.Permissions.Add(new Permission
            {
                Id = Guid.NewGuid(),
                Code = def.Code,
                Description = def.Description,
                Module = def.Module,
                Level = def.DefaultLevel
            });
            logger.LogInformation("Seeded permission {Code} (Level {Level})", def.Code, def.DefaultLevel);
        }

        await db.SaveChangesAsync();
    }

    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // 1. Seed roles (kèm Level)
        foreach (var roleName in AppRoles.All)
        {
            var defaultLevel = AppRoles.DefaultLevels[roleName];
            var role = await roleManager.FindByNameAsync(roleName);

            if (role is not null)
            {
                // Role có từ trước khi có cột Level (Level = 0): điền Level mặc định. Level đã đặt thì giữ nguyên.
                if (role.Level == 0)
                {
                    role.Level = defaultLevel;
                    var update = await roleManager.UpdateAsync(role);
                    if (update.Succeeded)
                        logger.LogInformation("Set Level {Level} for role {Role}", defaultLevel, roleName);
                    else
                        logger.LogError("Không cập nhật được Level của role {Role}: {Errors}", roleName,
                            string.Join("; ", update.Errors.Select(e => e.Description)));
                }
                continue;
            }

            var result = await roleManager.CreateAsync(
                new ApplicationRole(roleName) { Id = Guid.NewGuid(), Level = defaultLevel });
            if (result.Succeeded)
                logger.LogInformation("Seeded role {Role} (Level {Level})", roleName, defaultLevel);
            else
                logger.LogError("Không tạo được role {Role}: {Errors}", roleName,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        // 2. Seed tài khoản admin (lấy từ biến môi trường)
        var email = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
        var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("ADMIN_EMAIL/ADMIN_PASSWORD chưa được cấu hình, bỏ qua seed admin.");
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = Environment.GetEnvironmentVariable("ADMIN_FULLNAME") ?? "Administrator"
            };

            var create = await userManager.CreateAsync(admin, password);
            if (!create.Succeeded)
            {
                logger.LogError("Không tạo được admin: {Errors}",
                    string.Join("; ", create.Errors.Select(e => e.Description)));
                return;
            }
            logger.LogInformation("Seeded admin {Email}", email);
        }

        if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
            await userManager.AddToRoleAsync(admin, AppRoles.Admin);
    }
}
