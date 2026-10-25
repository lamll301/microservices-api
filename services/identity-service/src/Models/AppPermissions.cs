namespace IdentityService.Models;

/// <summary>Khai báo một permission trong code. <see cref="DefaultLevel"/> chỉ dùng khi tạo mới trong DB.</summary>
public sealed record PermissionDefinition(string Code, string Description, string Module, int DefaultLevel);

/// <summary>
/// Danh mục permission khai báo trong code. Quy ước tên: {service}.{resource}.{action}.
/// Seeder thêm permission còn thiếu vào bảng Permissions; Level trong DB mới là giá trị có hiệu lực.
/// </summary>
public static class AppPermissions
{
    public static class Identity
    {
        public const string UsersRead = "identity.users.read";
        public const string RolesRead = "identity.roles.read";
        public const string RolesAssign = "identity.roles.assign";
        public const string PermissionsRead = "identity.permissions.read";
        public const string PermissionsManage = "identity.permissions.manage";
    }

    public static readonly IReadOnlyList<PermissionDefinition> Definitions =
    [
        new(Identity.UsersRead, "Xem danh sách người dùng", "identity", 1000),
        new(Identity.RolesRead, "Xem danh sách role", "identity", 1000),
        new(Identity.RolesAssign, "Gán / gỡ role cho người dùng", "identity", AccessLevels.Admin),
        new(Identity.PermissionsRead, "Xem danh mục permission và Level", "identity", 1000),
        new(Identity.PermissionsManage, "Thay đổi Level của permission", "identity", AccessLevels.Admin)
    ];
}
