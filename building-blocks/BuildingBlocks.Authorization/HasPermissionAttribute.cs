using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Authorization;

/// <summary>Yêu cầu Level của user >= Level của permission. Ví dụ: [HasPermission(AppPermissions.Identity.UsersRead)].</summary>
public class HasPermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public HasPermissionAttribute(string permission) : base(PolicyPrefix + permission) { }
}
