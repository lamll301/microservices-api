using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.DTOs;
using IdentityService.Services;
using IdentityService.Tests.Infrastructure;

namespace IdentityService.Tests.Integration;

// Integration test: gọi API thật (qua HTTP trong bộ nhớ) + PostgreSQL thật trong Docker.
[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AuthFlowTests
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public AuthFlowTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_endpoint_returns_200()
    {
        var response = await _client.GetAsync("/health");

        await response.ShouldBeAsync(HttpStatusCode.OK);
    }

    // ---------- đăng ký ----------

    [Fact]
    public async Task Register_creates_user_with_default_role_and_sends_confirmation_email()
    {
        var email = IdentityApiFactory.NewEmail();

        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = IdentityApiFactory.DefaultPassword, fullName = "Nguyen Van A" });

        await response.ShouldBeAsync(HttpStatusCode.Created);
        var body = await response.ReadAsAsync<RegisterResult>();
        Assert.Equal(email, body.User.Email);
        Assert.Contains("User", body.User.Roles);

        var mail = _factory.Emails.LastTo(email);
        Assert.NotNull(mail);
        Assert.Contains("confirm-email", mail!.HtmlBody);
    }

    [Fact]
    public async Task Register_with_existing_email_returns_409()
    {
        var existing = await _factory.CreateUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = existing.Email, password = IdentityApiFactory.DefaultPassword });

        await response.ShouldBeAsync(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("short")]           // ngắn hơn 8 ký tự (DataAnnotations)
    [InlineData("alllowercase1")]   // thiếu chữ hoa (Identity)
    [InlineData("NOLOWERCASE1")]    // thiếu chữ thường (Identity)
    [InlineData("NoDigitsHere")]    // thiếu chữ số (Identity)
    public async Task Register_with_weak_password_returns_400(string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = IdentityApiFactory.NewEmail(), password });

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    // ---------- đăng nhập ----------

    [Fact]
    public async Task Login_before_email_confirmation_returns_403_with_code()
    {
        var user = await _factory.CreateUserAsync(emailConfirmed: false);

        var response = await _client.LoginAsync(user.Email, user.Password);

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
        var json = await response.ReadAsAsync<JsonElement>();
        Assert.Equal("email_not_confirmed", json.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_even_when_email_is_unconfirmed()
    {
        // Không được để lộ "tài khoản này tồn tại nhưng chưa xác thực" cho người nhập sai mật khẩu
        var user = await _factory.CreateUserAsync(emailConfirmed: false);

        var response = await _client.LoginAsync(user.Email, "WrongPassw0rd");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_unknown_email_returns_401()
    {
        var response = await _client.LoginAsync(IdentityApiFactory.NewEmail(), IdentityApiFactory.DefaultPassword);

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Too_many_wrong_passwords_lock_the_account()
    {
        var user = await _factory.CreateUserAsync();

        for (var i = 0; i < 5; i++)
            await _client.LoginAsync(user.Email, "WrongPassw0rd");

        // Bị khóa rồi thì ngay cả mật khẩu đúng cũng không vào được
        var response = await _client.LoginAsync(user.Email, user.Password);

        await response.ShouldBeAsync(HttpStatusCode.Locked);
    }

    // ---------- xác thực email ----------

    [Fact]
    public async Task Register_confirm_email_login_and_call_me()
    {
        var email = IdentityApiFactory.NewEmail();
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = IdentityApiFactory.DefaultPassword });

        // 1. Chưa xác thực -> chưa đăng nhập được
        await (await _client.LoginAsync(email, IdentityApiFactory.DefaultPassword))
            .ShouldBeAsync(HttpStatusCode.Forbidden);

        // 2. "Bấm" link trong email
        var link = EmailLinks.FirstLink(_factory.Emails.LastTo(email)!);
        var confirm = await _client.GetAsync(link.PathAndQuery);
        await confirm.ShouldBeAsync(HttpStatusCode.OK);

        // 3. Đăng nhập được và có đủ token
        var auth = await _client.LoginOkAsync(email);
        Assert.False(string.IsNullOrEmpty(auth.AccessToken));
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));
        Assert.Contains("User", auth.User.Roles);

        // 4. Dùng access token gọi endpoint cần đăng nhập
        var me = await _factory.CreateAuthedClient(auth.AccessToken).GetAsync("/api/auth/me");
        await me.ShouldBeAsync(HttpStatusCode.OK);
        var info = await me.ReadAsAsync<UserInfo>();
        Assert.Equal(email, info.Email);
    }

    [Fact]
    public async Task Confirm_email_with_tampered_token_returns_400()
    {
        var email = IdentityApiFactory.NewEmail();
        await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = IdentityApiFactory.DefaultPassword });
        var link = EmailLinks.FirstLink(_factory.Emails.LastTo(email)!);

        var response = await _client.GetAsync(
            $"/api/auth/confirm-email?userId={link.QueryValue("userId")}&token={TokenCodec.Encode("garbage")}");

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_without_token_returns_401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }
}
