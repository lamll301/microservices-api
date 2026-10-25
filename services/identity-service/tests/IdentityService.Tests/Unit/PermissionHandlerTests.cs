using System.Security.Claims;
using BuildingBlocks.Authorization;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace IdentityService.Tests.Unit;

public class PermissionHandlerTests
{
    private const string UsersRead = "identity.users.read";

    private sealed class FakeLevelProvider : IPermissionLevelProvider
    {
        private readonly Dictionary<string, int> _levels = new() { [UsersRead] = 1000 };

        public Task<int?> GetRequiredLevelAsync(string code, CancellationToken ct = default) =>
            Task.FromResult(_levels.TryGetValue(code, out var level) ? level : (int?)null);
    }

    private static async Task<bool> EvaluateAsync(string? levelClaim, string permission = UsersRead)
    {
        var handler = new PermissionHandler(new FakeLevelProvider());
        var requirement = new PermissionRequirement(permission);

        var claims = new List<Claim>();
        if (levelClaim is not null)
            claims.Add(new Claim(AuthClaimTypes.Level, levelClaim));

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Theory]
    [InlineData("9999", true)]   // SuperAdmin
    [InlineData("9998", true)]   // Admin
    [InlineData("1000", true)]   // đúng bằng mức yêu cầu
    [InlineData("999", false)]
    [InlineData("899", false)]   // Manager
    [InlineData("100", false)]   // User
    [InlineData("0", false)]
    public async Task Level_duoc_so_sanh_voi_muc_yeu_cau(string level, bool expected)
    {
        Assert.Equal(expected, await EvaluateAsync(level));
    }

    [Theory]
    [InlineData(null)]       // thiếu claim
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("99.9")]
    public async Task Claim_level_thieu_hoac_khong_hop_le_thi_tu_choi(string? level)
    {
        Assert.False(await EvaluateAsync(level));
    }

    [Fact]
    public async Task Permission_chua_khai_bao_thi_tu_choi_ke_ca_level_cao_nhat()
    {
        Assert.False(await EvaluateAsync("9999", "orders.order.create"));
    }
}
