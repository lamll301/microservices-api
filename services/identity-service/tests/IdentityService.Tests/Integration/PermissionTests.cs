using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IdentityService.Tests.Infrastructure;

namespace IdentityService.Tests.Integration;

[Collection(ApiCollection.Name)]
[Trait("Category", "Integration")]
public class PermissionTests
{
    // Permission không test nào khác phụ thuộc; mặc định Level 1000. Test đổi Level phải khôi phục lại.
    private const string TestPermission = "identity.permissions.read";
    private const int DefaultLevel = 1000;

    private readonly IdentityApiFactory _factory;
    private readonly HttpClient _client;

    public PermissionTests(IdentityApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var admin = await _client.LoginOkAsync(IdentityApiFactory.AdminEmail, IdentityApiFactory.AdminPassword);
        return _factory.CreateAuthedClient(admin.AccessToken);
    }

    private async Task<HttpClient> ClientForRoleAsync(string role)
    {
        var user = await _factory.CreateUserAsync(role: role);
        var session = await _client.LoginOkAsync(user.Email);
        return _factory.CreateAuthedClient(session.AccessToken);
    }

    private static Task<HttpResponseMessage> SetLevelAsync(HttpClient client, string code, int level) =>
        client.PutAsJsonAsync($"/api/admin/permissions/{code}/level", new { level });

    private async Task<JsonElement> InternalSnapshotAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/internal/permissions");
        request.Headers.Add("X-Internal-Key", IdentityApiFactory.InternalApiKey);
        var response = await _client.SendAsync(request);
        await response.ShouldBeAsync(HttpStatusCode.OK);
        return await response.ReadAsAsync<JsonElement>();
    }

    // ---------- token ----------

    [Theory]
    [InlineData("User", 100)]
    [InlineData("Manager", 899)]
    [InlineData("Admin", 9998)]
    public async Task Access_token_carries_the_highest_role_level(string role, int expected)
    {
        var user = await _factory.CreateUserAsync(role: role);

        var session = await _client.LoginOkAsync(user.Email);

        var claim = new JwtSecurityTokenHandler().ReadJwtToken(session.AccessToken)
            .Claims.First(c => c.Type == "level").Value;
        Assert.Equal(expected, int.Parse(claim));
    }

    // ---------- kiểm tra Level ----------

    [Fact]
    public async Task Manager_is_below_the_level_required_for_user_list()
    {
        var manager = await ClientForRoleAsync("Manager");

        await (await manager.GetAsync("/api/admin/users")).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_can_list_permissions_with_levels()
    {
        var admin = await AdminClientAsync();

        var response = await admin.GetAsync("/api/admin/permissions");

        await response.ShouldBeAsync(HttpStatusCode.OK);
        var json = await response.ReadAsAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        var users = json.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("code").GetString() == "identity.users.read");
        Assert.Equal(1000, users.GetProperty("level").GetInt32());
    }

    [Fact]
    public async Task Changing_a_permission_level_takes_effect_immediately_for_existing_tokens()
    {
        var admin = await AdminClientAsync();
        var manager = await ClientForRoleAsync("Manager");   // level 899, token đã cấp từ trước khi đổi

        await (await manager.GetAsync("/api/admin/permissions")).ShouldBeAsync(HttpStatusCode.Forbidden);

        try
        {
            await (await SetLevelAsync(admin, TestPermission, 500)).ShouldBeAsync(HttpStatusCode.OK);

            await (await manager.GetAsync("/api/admin/permissions")).ShouldBeAsync(HttpStatusCode.OK);
        }
        finally
        {
            await (await SetLevelAsync(admin, TestPermission, DefaultLevel)).ShouldBeAsync(HttpStatusCode.OK);
        }

        await (await manager.GetAsync("/api/admin/permissions")).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    // ---------- đổi Level của permission ----------

    [Fact]
    public async Task Changing_a_level_changes_the_snapshot_version()
    {
        var admin = await AdminClientAsync();
        var before = (await InternalSnapshotAsync()).GetProperty("version").GetString();

        try
        {
            await (await SetLevelAsync(admin, TestPermission, 1200)).ShouldBeAsync(HttpStatusCode.OK);
            var after = (await InternalSnapshotAsync()).GetProperty("version").GetString();
            Assert.NotEqual(before, after);
        }
        finally
        {
            await SetLevelAsync(admin, TestPermission, DefaultLevel);
        }

        Assert.Equal(before, (await InternalSnapshotAsync()).GetProperty("version").GetString());
    }

    [Fact]
    public async Task Admin_cannot_change_a_permission_at_their_own_level()
    {
        var admin = await AdminClientAsync();

        // identity.roles.assign đang ở 9998 = cấp của Admin -> chỉ SuperAdmin được sửa
        await (await SetLevelAsync(admin, "identity.roles.assign", 100)).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_cannot_set_a_level_above_their_own()
    {
        var admin = await AdminClientAsync();

        await (await SetLevelAsync(admin, TestPermission, 9999)).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public async Task Level_outside_the_valid_range_returns_400(int level)
    {
        var admin = await AdminClientAsync();

        await (await SetLevelAsync(admin, TestPermission, level)).ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changing_the_level_of_an_unknown_permission_returns_404()
    {
        var admin = await AdminClientAsync();

        await (await SetLevelAsync(admin, "identity.does.not.exist", 100)).ShouldBeAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Normal_user_cannot_change_permission_levels()
    {
        var user = await ClientForRoleAsync("User");

        await (await SetLevelAsync(user, TestPermission, 1)).ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    // ---------- chống leo thang đặc quyền khi gán role ----------

    [Theory]
    [InlineData("Admin")]
    [InlineData("SuperAdmin")]
    public async Task Admin_cannot_assign_a_role_at_or_above_their_own_level(string role)
    {
        var admin = await AdminClientAsync();
        var user = await _factory.CreateUserAsync();

        var response = await admin.PostAsJsonAsync($"/api/admin/users/{user.Id}/roles", new { role });

        await response.ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_cannot_manage_another_admin()
    {
        var admin = await AdminClientAsync();
        var otherAdmin = await _factory.CreateUserAsync(role: "Admin");

        await (await admin.PostAsJsonAsync($"/api/admin/users/{otherAdmin.Id}/roles", new { role = "Manager" }))
            .ShouldBeAsync(HttpStatusCode.Forbidden);
        await (await admin.DeleteAsync($"/api/admin/users/{otherAdmin.Id}/roles/Admin"))
            .ShouldBeAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_cannot_remove_any_of_their_own_roles()
    {
        var login = await _client.LoginOkAsync(IdentityApiFactory.AdminEmail, IdentityApiFactory.AdminPassword);
        var admin = _factory.CreateAuthedClient(login.AccessToken);

        await (await admin.DeleteAsync($"/api/admin/users/{login.User.Id}/roles/User"))
            .ShouldBeAsync(HttpStatusCode.BadRequest);
    }

    // ---------- endpoint nội bộ ----------

    [Fact]
    public async Task Internal_snapshot_requires_the_internal_key()
    {
        await (await _client.GetAsync("/api/internal/permissions")).ShouldBeAsync(HttpStatusCode.Unauthorized);

        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/internal/permissions");
        wrong.Headers.Add("X-Internal-Key", "wrong-key");
        await (await _client.SendAsync(wrong)).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Internal_snapshot_returns_version_and_all_permissions()
    {
        var json = await InternalSnapshotAsync();

        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        var codes = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()).ToList();
        Assert.Contains("identity.users.read", codes);
        Assert.Contains("identity.roles.assign", codes);
    }

    [Fact]
    public async Task Internal_snapshot_does_not_accept_a_user_token()
    {
        var admin = await AdminClientAsync();

        await (await admin.GetAsync("/api/internal/permissions")).ShouldBeAsync(HttpStatusCode.Unauthorized);
    }
}
