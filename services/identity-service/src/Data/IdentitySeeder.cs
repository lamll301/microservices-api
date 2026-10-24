using IdentityService.Models;
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // 1. Seed roles
        foreach (var roleName in AppRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName)) continue;

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.NewGuid() });
            if (result.Succeeded)
                logger.LogInformation("Seeded role {Role}", roleName);
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
