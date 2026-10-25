namespace IdentityService.Models;

public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";      // 9999
    public const string Admin = "Admin";       // 9998
    // public const string Director = "Director";   // 999
    public const string Manager = "Manager";     // 899
    // public const string Staff = "Staff";     // 499
    public const string User = "User";      // 99
    // public const string Guest = "Guest";
    public static readonly string[] All = [SuperAdmin, Admin, Manager, User];

    // Auditor (Nếu cần kiểm toán): Chỉ xem nhật ký, lịch sử thay đổi để kiểm tra
    // Support (Nếu có bộ phận hỗ trợ): Hỗ trợ người dùng, xử lý sự cố tài khoản

    /// <summary>
    /// Level mặc định khi seed role. Sau khi đã có trong DB thì DB là nguồn sự thật
    /// (seeder chỉ điền Level cho role cũ chưa có Level, không ghi đè giá trị đã đặt).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> DefaultLevels = new Dictionary<string, int>
    {
        [SuperAdmin] = AccessLevels.SuperAdmin,
        [Admin] = AccessLevels.Admin,
        [Manager] = AccessLevels.Manager,
        [User] = AccessLevels.User
    };
}
