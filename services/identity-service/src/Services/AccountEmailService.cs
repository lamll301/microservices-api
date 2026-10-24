using System.Net;
using IdentityService.Models;

namespace IdentityService.Services;

public interface IAccountEmailService
{
    Task SendConfirmationAsync(ApplicationUser user, string token, CancellationToken ct = default);
    Task SendPasswordResetAsync(ApplicationUser user, string token, CancellationToken ct = default);
}

public class AccountEmailService : IAccountEmailService
{
    private readonly IEmailService _email;
    private readonly AccountSettings _settings;

    public AccountEmailService(IEmailService email, AccountSettings settings)
    {
        _email = email;
        _settings = settings;
    }

    public Task SendConfirmationAsync(ApplicationUser user, string token, CancellationToken ct = default)
    {
        // Link trỏ thẳng vào API: bấm là xác thực xong
        var link = $"{_settings.ApiPublicUrl}/api/auth/confirm-email?userId={user.Id}&token={TokenCodec.Encode(token)}";

        var html = Layout(
            greetingName: user.FullName ?? user.Email,
            intro: "Cảm ơn bạn đã đăng ký tài khoản. Nhấn nút bên dưới để xác thực địa chỉ email của bạn.",
            buttonText: "Xác thực email",
            link: link,
            footer: $"Liên kết có hiệu lực trong {_settings.TokenLifespanHours} giờ. Nếu bạn không đăng ký tài khoản, hãy bỏ qua email này.");

        return _email.SendAsync(user.Email!, "Xác thực email của bạn", html, ct);
    }

    public Task SendPasswordResetAsync(ApplicationUser user, string token, CancellationToken ct = default)
    {
        // Link trỏ tới trang frontend; trang đó gọi POST /api/auth/reset-password với email + token trong URL
        var link = $"{_settings.FrontendUrl}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={TokenCodec.Encode(token)}";

        var html = Layout(
            greetingName: user.FullName ?? user.Email,
            intro: "Chúng tôi nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn. Nhấn nút bên dưới để đặt mật khẩu mới.",
            buttonText: "Đặt lại mật khẩu",
            link: link,
            footer: $"Liên kết có hiệu lực trong {_settings.TokenLifespanHours} giờ. Nếu bạn không yêu cầu, hãy bỏ qua email này, mật khẩu của bạn vẫn an toàn.");

        return _email.SendAsync(user.Email!, "Đặt lại mật khẩu", html, ct);
    }

    private static string Layout(string? greetingName, string intro, string buttonText, string link, string footer)
    {
        var name = WebUtility.HtmlEncode(greetingName ?? string.Empty);
        var href = WebUtility.HtmlEncode(link);

        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:0 auto;padding:24px;color:#222">
              <p>Xin chào {name},</p>
              <p>{WebUtility.HtmlEncode(intro)}</p>
              <p style="margin:28px 0">
                <a href="{href}" style="background:#2563eb;color:#fff;text-decoration:none;padding:12px 20px;border-radius:6px;display:inline-block">{WebUtility.HtmlEncode(buttonText)}</a>
              </p>
              <p style="font-size:13px;color:#555">Nếu nút không hoạt động, hãy sao chép liên kết sau vào trình duyệt:<br>
                <span style="word-break:break-all">{href}</span></p>
              <p style="font-size:13px;color:#888">{WebUtility.HtmlEncode(footer)}</p>
            </div>
            """;
    }
}
