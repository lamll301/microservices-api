using System.Net;
using System.Net.Http.Json;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IdentityService.Tests.Integration;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class RefreshTokenTests
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public RefreshTokenTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_old_one_stops_working()
    {
        var user = await _factory.CreateUserAsync();
        var first = await _client.LoginOkAsync(user.Email);

        var refreshed = await _client.RefreshAsync(first.RefreshToken);

        await refreshed.ShouldBeAsync(HttpStatusCode.OK);
        var second = await refreshed.ReadAsAsync<AuthResponse>();
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.NotEqual(first.AccessToken, second.AccessToken);

        // Token cũ đã bị thu hồi
        await (await _client.RefreshAsync(first.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reusing_a_revoked_token_revokes_the_whole_session_family()
    {
        var user = await _factory.CreateUserAsync();
        var first = await _client.LoginOkAsync(user.Email);
        var second = await (await _client.RefreshAsync(first.RefreshToken)).ReadAsAsync<AuthResponse>();

        // Kẻ trộm dùng lại token cũ -> bị phát hiện
        await (await _client.RefreshAsync(first.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);

        // ...và token mới (hợp lệ trước đó) cũng bị thu hồi theo
        await (await _client.RefreshAsync(second.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_unknown_token_returns_401()
    {
        var response = await _client.RefreshAsync("this-token-does-not-exist");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_with_expired_token_returns_401()
    {
        var user = await _factory.CreateUserAsync();
        var auth = await _client.LoginOkAsync(user.Email);

        // Giả lập token hết hạn bằng cách sửa thẳng trong DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.RefreshTokens
                .Where(t => t.UserId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddDays(-1)));
        }

        var response = await _client.RefreshAsync(auth.RefreshToken);

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var user = await _factory.CreateUserAsync();
        var auth = await _client.LoginOkAsync(user.Email);

        var logout = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = auth.RefreshToken });

        await logout.ShouldBeAsync(HttpStatusCode.NoContent);
        await (await _client.RefreshAsync(auth.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_all_revokes_every_device()
    {
        var user = await _factory.CreateUserAsync();
        var phone = await _client.LoginOkAsync(user.Email);
        var laptop = await _client.LoginOkAsync(user.Email);

        var logoutAll = await _factory.CreateAuthedClient(phone.AccessToken).PostAsync("/api/auth/logout-all", null);

        await logoutAll.ShouldBeAsync(HttpStatusCode.NoContent);
        await (await _client.RefreshAsync(phone.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
        await (await _client.RefreshAsync(laptop.RefreshToken)).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_all_requires_authentication()
    {
        var response = await _client.PostAsync("/api/auth/logout-all", null);

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }
}
