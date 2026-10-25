using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Authorization;

public static class PermissionAuthorizationExtensions
{
    /// <summary>
    /// Đăng ký phân quyền theo Level: <see cref="PermissionHandler"/> + policy động cho [HasPermission].
    /// Mỗi service phải tự đăng ký thêm một <see cref="IPermissionLevelProvider"/> (nguồn cho biết Level của permission).
    /// </summary>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        return services;
    }
}
