namespace IdentityService.Models;

/// <summary>
/// Các mức cấp bậc (Level). Level càng cao quyền càng lớn.
/// Level của user = Level cao nhất trong các role của user.
/// </summary>
public static class AccessLevels
{
    public const int SuperAdmin = 9999;
    public const int Admin = 9998;
    // public const int Director = 999;
    public const int Manager = 899;
    // public const int Staff = 499;
    public const int User = 100;

    /// <summary>Khoảng Level hợp lệ khi đặt cho permission.</summary>
    public const int Min = 1;
    public const int Max = 9999;
}
