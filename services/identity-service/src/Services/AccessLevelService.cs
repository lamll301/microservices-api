using IdentityService.Data;
using IdentityService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public interface IAccessLevelService
{
    /// <summary>Level cao nhất trong các role. Không có role (hoặc role không tồn tại) thì trả 0.</summary>
    Task<int> GetLevelAsync(IEnumerable<string> roleNames, CancellationToken ct = default);

    /// <summary>Level hiện tại của user, tính từ role trong DB (không dựa vào claim trong token).</summary>
    Task<int> GetUserLevelAsync(ApplicationUser user, CancellationToken ct = default);
}

public class AccessLevelService : IAccessLevelService
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public AccessLevelService(AppDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<int> GetLevelAsync(IEnumerable<string> roleNames, CancellationToken ct = default)
    {
        var names = roleNames.ToList();
        if (names.Count == 0) return 0;

        var max = await _db.Roles
            .AsNoTracking()
            .Where(r => r.Name != null && names.Contains(r.Name))
            .MaxAsync(r => (int?)r.Level, ct);

        return max ?? 0;
    }

    public async Task<int> GetUserLevelAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return await GetLevelAsync(roles, ct);
    }
}
