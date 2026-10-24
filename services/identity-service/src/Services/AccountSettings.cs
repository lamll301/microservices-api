namespace IdentityService.Services;

public class AccountSettings
{
    /// <summary>Bắt buộc xác thực email mới được đăng nhập.</summary>
    public bool RequireConfirmedEmail { get; init; } = true;

    /// <summary>Thời gian sống của token xác thực email / đặt lại mật khẩu (giờ).</summary>
    public int TokenLifespanHours { get; init; } = 2;

    /// <summary>URL public của API, dùng cho link xác thực email (người dùng bấm link sẽ gọi thẳng API).</summary>
    public string ApiPublicUrl { get; init; } = "http://localhost:5137";

    /// <summary>URL frontend, dùng cho link đặt lại mật khẩu (trang nhập mật khẩu mới).</summary>
    public string FrontendUrl { get; init; } = "http://localhost:3000";

    public static AccountSettings FromEnvironment()
    {
        static string Url(string name, string fallback) =>
            (Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback).TrimEnd('/');

        return new AccountSettings
        {
            RequireConfirmedEmail = !string.Equals(
                Environment.GetEnvironmentVariable("REQUIRE_CONFIRMED_EMAIL"), "false", StringComparison.OrdinalIgnoreCase),
            TokenLifespanHours =
                int.TryParse(Environment.GetEnvironmentVariable("EMAIL_TOKEN_LIFESPAN_HOURS"), out var h) && h > 0 ? h : 2,
            ApiPublicUrl = Url("API_PUBLIC_URL", "http://localhost:5137"),
            FrontendUrl = Url("FRONTEND_URL", "http://localhost:3000")
        };
    }
}
