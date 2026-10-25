using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.IdentityModel.Tokens.Jwt;
using IdentityService.Tests.Infrastructure;

namespace IdentityService.Tests.Integration;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class AdminTests
{
    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public AdminTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var admin = await _client.LoginOkAsync(IdentityApiFactory.AdminEmail, IdentityApiFactory.AdminPassword);
        return _factory.CreateAuthedClient(admin.AccessToken);
    }

    private static async Task<string[]> ReadRolesAsync(HttpResponseMessage response)
    {
        var json = await response.ReadAsAsync<JsonElement>();
        return json.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();
    }

    // ---------- phân quyền ----------

    [Fact]
    public async Task Admin_endpoints_require_a_token()
    {
        var response = await _client.GetAsync("/api/admin/users");

        await response.ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_endpoints_reject_normal_users()
    {
        var user = await _factory.CreateUserAsync();
        var session = await _client.LoginOkAsync(user.Email);

        var response = await _factory.CreateAuthedClient(session.AccessToken).GetAsync("/api/admin/users");

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    // ---------- seed ----------

    [Fact]
    public async Task Seeded_admin_exists_and_can_list_users()
    {
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/admin/users?page=1&pageSize=100");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<JsonElement>();
        Assert.True(json.GetProperty("total").GetInt32() >= 1);

        var adminRow = json.GetProperty("items").EnumerateArray()
            .FirstOrDefault(i => i.GetProperty("email").GetString() == IdentityApiFactory.AdminEmail);
        Assert.Equal(JsonValueKind.Object, adminRow.ValueKind);
        Assert.Contains(adminRow.GetProperty("roles").EnumerateArray(), r => r.GetString() == "Admin");
    }

    [Fact]
    public async Task Admin_can_list_roles()
    {
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/admin/roles");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var roles = (await response.ReadAsAsync<JsonElement>()).EnumerateArray().Select(r => r.GetString()).ToList();
        Assert.Contains("Admin", roles);
        Assert.Contains("User", roles);
    }

    // ---------- gán / gỡ role ----------

    [Fact]
    public async Task Admin_can_assign_and_remove_a_lower_level_role()
    {
        var admin = await AdminClientAsync();
        var user = await _factory.CreateUserAsync();
        var rolesUrl = $"/api/admin/users/{user.Id}/roles";

        var assign = await admin.PostAsJsonAsync(rolesUrl, new { role = "Manager" });
        await assign.ShouldBeAsync(HttpStatusCode.OK);
        Assert.Contains("Manager", await ReadRolesAsync(assign));

        // Gán lần nữa -> trùng
        await (await admin.PostAsJsonAsync(rolesUrl, new { role = "Manager" })).ShouldBeAsync(HttpStatusCode.Conflict);

        // Role không tồn tại
        await (await admin.PostAsJsonAsync(rolesUrl, new { role = "Wizard" })).ShouldBeAsync(HttpStatusCode.BadRequest);

        var remove = await admin.DeleteAsync($"{rolesUrl}/Manager");
        await remove.ShouldBeAsync(HttpStatusCode.OK);
        Assert.DoesNotContain("Manager", await ReadRolesAsync(remove));

        // Gỡ lần nữa -> không còn role đó
        await (await admin.DeleteAsync($"{rolesUrl}/Manager")).ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Assign_role_to_unknown_user_returns_404()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/admin/users/{Guid.NewGuid()}/roles", new { role = "Admin" });

        await response.ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_cannot_remove_their_own_admin_role()
    {
        var login = await _client.LoginOkAsync(IdentityApiFactory.AdminEmail, IdentityApiFactory.AdminPassword);
        var admin = _factory.CreateAuthedClient(login.AccessToken);

        var response = await admin.DeleteAsync($"/api/admin/users/{login.User.Id}/roles/Admin");

        await response.ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Role_change_takes_effect_only_after_the_token_is_refreshed()
    {
        var admin = await AdminClientAsync();
        var user = await _factory.CreateUserAsync();
        var session = await _client.LoginOkAsync(user.Email);

        await (await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/roles", new { role = "Manager" }))
            .ShouldBeAsync(HttpStatusCode.OK);

        // Role và level nằm trong access token -> token cũ vẫn mang level của role User
        Assert.Equal(100, LevelOf(session.AccessToken));

        // Làm mới token -> token mới mang level của Manager
        var refreshed = await (await _client.RefreshAsync(session.RefreshToken)).ReadAsAsync<IdentityService.DTOs.AuthResponse>();
        Assert.Equal(899, LevelOf(refreshed.AccessToken));
    }

    private static int LevelOf(string accessToken) =>
        int.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.First(c => c.Type == "level").Value);
}
