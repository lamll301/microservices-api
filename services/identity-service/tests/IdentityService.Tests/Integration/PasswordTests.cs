using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.DTOs;
using IdentityService.Services;
using IdentityService.Tests.Infrastructure;

namespace IdentityService.Tests.Integration;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class PasswordTests
{
    private const string NewPassword = "NewPassw0rd456";

    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public PasswordTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ---------- quên / đặt lại mật khẩu ----------

    [Fact]
    public async Task Forgot_password_gives_the_same_answer_for_known_and_unknown_emails()
    {
        var known = await _factory.CreateUserAsync();
        var unknownEmail = IdentityApiFactory.NewEmail();

        var forKnown = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = known.Email });
        var forUnknown = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = unknownEmail });

        await forKnown.ShouldBeAsync(HttpStatusCode.OK);
        await forUnknown.ShouldBeAsync(HttpStatusCode.OK);

        // Hai câu trả lời giống hệt nhau -> kẻ xấu không dò được email nào đã đăng ký
        Assert.Equal(
            await forKnown.Content.ReadAsStringAsync(),
            await forUnknown.Content.ReadAsStringAsync());

        // Nhưng chỉ email có thật mới nhận được thư
        Assert.Single(_factory.Emails.To(known.Email));
        Assert.Empty(_factory.Emails.To(unknownEmail));
    }

    [Fact]
    public async Task Forgot_then_reset_changes_password_and_revokes_sessions()
    {
        var user = await _factory.CreateUserAsync();
        var oldSession = await _client.LoginOkAsync(user.Email);

        await (await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email }))
            .ShouldBeAsync(HttpStatusCode.OK);

        var link = EmailLinks.FirstLink(_factory.Emails.LastTo(user.Email)!);
        Assert.Equal(IdentityApiFactory.FrontendUrl + "/reset-password", link.GetLeftPart(UriPartial.Path));

        var reset = await _client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = link.QueryValue("email"),
            token = link.QueryValue("token"),
            newPassword = NewPassword
        });
        await reset.ShouldBeAsync(HttpStatusCode.OK);

        // Mật khẩu cũ hết tác dụng, mật khẩu mới dùng được
        await (await _client.LoginAsync(user.Email, user.Password)).ShouldBeAsync(HttpStatusCode.Unauthorized);
        await _client.LoginOkAsync(user.Email, NewPassword);

        // Phiên đăng nhập cũ bị đá ra
        await (await _client.RefreshAsync(oldSession.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_token_can_only_be_used_once()
    {
        var user = await _factory.CreateUserAsync();
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email });
        var link = EmailLinks.FirstLink(_factory.Emails.LastTo(user.Email)!);
        var payload = new
        {
            email = link.QueryValue("email"),
            token = link.QueryValue("token"),
            newPassword = NewPassword
        };

        await (await _client.PostAsJsonAsync("/api/auth/reset-password", payload)).ShouldBeAsync(HttpStatusCode.OK);
        await (await _client.PostAsJsonAsync("/api/auth/reset-password", payload)).ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reset_with_invalid_token_returns_400()
    {
        var user = await _factory.CreateUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = user.Email,
            token = TokenCodec.Encode("garbage"),
            newPassword = NewPassword
        });

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reset_with_weak_new_password_returns_400()
    {
        var user = await _factory.CreateUserAsync();
        await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email = user.Email });
        var link = EmailLinks.FirstLink(_factory.Emails.LastTo(user.Email)!);

        var response = await _client.PostAsJsonAsync("/api/auth/reset-password", new
        {
            email = link.QueryValue("email"),
            token = link.QueryValue("token"),
            newPassword = "alllowercase1"
        });

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
        // Mật khẩu cũ vẫn dùng được
        await _client.LoginOkAsync(user.Email, user.Password);
    }

    // ---------- đổi mật khẩu ----------

    [Fact]
    public async Task Change_password_requires_the_current_password()
    {
        var user = await _factory.CreateUserAsync();
        var session = await _client.LoginOkAsync(user.Email);
        var authed = _factory.CreateAuthedClient(session.AccessToken);

        var response = await authed.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "WrongPassw0rd", newPassword = NewPassword });

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
        await _client.LoginOkAsync(user.Email, user.Password);   // mật khẩu cũ vẫn nguyên
    }

    [Fact]
    public async Task Change_password_logs_out_other_devices_and_returns_fresh_tokens()
    {
        var user = await _factory.CreateUserAsync();
        var session = await _client.LoginOkAsync(user.Email);
        var authed = _factory.CreateAuthedClient(session.AccessToken);

        var response = await authed.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = user.Password, newPassword = NewPassword });

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var fresh = await response.ReadAsAsync<AuthResponse>();

        // Thiết bị cũ thử refresh bằng token đã bị thu hồi -> bị từ chối...
        await (await _client.RefreshAsync(session.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
        // ...nhưng phiên mới vừa cấp sau khi đổi mật khẩu KHÔNG được bị kéo sập theo
        await (await _client.RefreshAsync(fresh.RefreshToken)).ShouldBeAsync(HttpStatusCode.OK);
        await (await _client.LoginAsync(user.Email, user.Password)).ShouldBeAsync(HttpStatusCode.Unauthorized);
        await _client.LoginOkAsync(user.Email, NewPassword);
    }

    [Fact]
    public async Task Change_password_requires_authentication()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "x", newPassword = NewPassword });

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    // ---------- gửi lại email xác thực ----------

    [Fact]
    public async Task Resend_confirmation_only_sends_mail_to_unconfirmed_existing_accounts()
    {
        var unknownEmail = IdentityApiFactory.NewEmail();
        var unconfirmed = await _factory.CreateUserAsync(emailConfirmed: false);
        var confirmed = await _factory.CreateUserAsync();

        foreach (var email in new[] { unknownEmail, unconfirmed.Email, confirmed.Email })
        {
            var response = await _client.PostAsJsonAsync("/api/auth/resend-confirmation", new { email });
            await response.ShouldBeAsync(HttpStatusCode.OK);   // luôn 200 để không lộ email nào tồn tại
        }

        Assert.Empty(_factory.Emails.To(unknownEmail));
        Assert.Single(_factory.Emails.To(unconfirmed.Email));
        Assert.Empty(_factory.Emails.To(confirmed.Email));
    }
}
