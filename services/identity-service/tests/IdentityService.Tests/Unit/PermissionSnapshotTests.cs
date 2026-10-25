using IdentityService.DTOs;
using IdentityService.Services;
using Xunit;

namespace IdentityService.Tests.Unit;

public class PermissionSnapshotTests
{
    private static PermissionDto P(string code, int level, string module = "identity") =>
        new() { Code = code, Description = "mô tả", Module = module, Level = level };

    [Fact]
    public void GetLevel_tra_ve_level_hoac_null_neu_khong_co()
    {
        var snapshot = new PermissionSnapshot([P("identity.users.read", 1000)]);

        Assert.Equal(1000, snapshot.GetLevel("identity.users.read"));
        Assert.Null(snapshot.GetLevel("identity.unknown"));
    }

    [Fact]
    public void Version_khong_phu_thuoc_thu_tu_dau_vao()
    {
        var a = new PermissionSnapshot([P("a.x", 1), P("b.y", 2)]);
        var b = new PermissionSnapshot([P("b.y", 2), P("a.x", 1)]);

        Assert.Equal(a.Version, b.Version);
    }

    [Fact]
    public void Version_doi_khi_level_doi()
    {
        var before = new PermissionSnapshot([P("a.x", 1)]);
        var after = new PermissionSnapshot([P("a.x", 2)]);

        Assert.NotEqual(before.Version, after.Version);
    }

    [Fact]
    public void Version_khong_doi_khi_chi_doi_mo_ta()
    {
        var a = new PermissionSnapshot([new PermissionDto { Code = "a.x", Description = "cũ", Module = "m", Level = 1 }]);
        var b = new PermissionSnapshot([new PermissionDto { Code = "a.x", Description = "mới", Module = "m", Level = 1 }]);

        Assert.Equal(a.Version, b.Version);
    }
}
