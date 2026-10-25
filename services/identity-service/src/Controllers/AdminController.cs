using System.Security.Claims;
using BuildingBlocks.Authorization;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly AppDbContext _db;
    private readonly IAccessLevelService _accessLevels;
    private readonly IPermissionService _permissions;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        AppDbContext db,
        IAccessLevelService accessLevels,
        IPermissionService permissions)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _accessLevels = accessLevels;
        _permissions = permissions;
    }

    // ================= Users / Roles =================

    [HttpGet("users")]
    [HasPermission(AppPermissions.Identity.UsersRead)]
    public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await _userManager.Users.CountAsync();
        var users = await _userManager.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ids = users.Select(u => u.Id).ToList();
        var roleRows = await (
            from ur in _db.UserRoles
            where ids.Contains(ur.UserId)
            join r in _db.Roles on ur.RoleId equals r.Id
            select new { ur.UserId, r.Name }).ToListAsync();

        var items = users.Select(u => new AdminUserDto
        {
            Id = u.Id,
            Email = u.Email ?? string.Empty,
            FullName = u.FullName,
            IsActive = u.IsActive,
            CreatedAtUtc = u.CreatedAtUtc,
            Roles = roleRows.Where(x => x.UserId == u.Id).Select(x => x.Name!).ToList()
        });

        return Ok(new { page, pageSize, total, items });
    }

    [HttpGet("roles")]
    [HasPermission(AppPermissions.Identity.RolesRead)]
    public async Task<IActionResult> GetRoles() =>
        Ok(await _roleManager.Roles.AsNoTracking().Select(r => r.Name).ToListAsync());

    [HttpPost("users/{userId:guid}/roles")]
    [HasPermission(AppPermissions.Identity.RolesAssign)]
    public async Task<IActionResult> AssignRole(Guid userId, [FromBody] AssignRoleRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { message = "Không tìm thấy user." });

        var role = await _roleManager.FindByNameAsync(request.Role);
        if (role is null)
            return BadRequest(new { message = $"Role '{request.Role}' không tồn tại." });

        // Chống leo thang đặc quyền: chỉ được thao tác với role và user có Level thấp hơn hẳn cấp của mình.
        // Level lấy từ DB (không dùng claim) để việc bị hạ cấp có hiệu lực ngay với thao tác ghi.
        var actorLevel = await GetActorLevelAsync();
        if (role.Level >= actorLevel)
            return Forbidden("Bạn chỉ được gán role có cấp thấp hơn cấp của mình.");
        if (await _accessLevels.GetUserLevelAsync(user) >= actorLevel)
            return Forbidden("Bạn chỉ được thao tác trên user có cấp thấp hơn cấp của mình.");

        if (await _userManager.IsInRoleAsync(user, role.Name!))
            return Conflict(new { message = "User đã có role này." });

        var result = await _userManager.AddToRoleAsync(user, role.Name!);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return Ok(new { userId, roles = await _userManager.GetRolesAsync(user) });
    }

    [HttpDelete("users/{userId:guid}/roles/{role}")]
    [HasPermission(AppPermissions.Identity.RolesAssign)]
    public async Task<IActionResult> RemoveRole(Guid userId, string role)
    {
        if (GetCurrentUserId() == userId)
            return BadRequest(new { message = "Không thể tự gỡ role của chính mình." });

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { message = "Không tìm thấy user." });

        var roleEntity = await _roleManager.FindByNameAsync(role);
        if (roleEntity is null || !await _userManager.IsInRoleAsync(user, roleEntity.Name!))
            return NotFound(new { message = "User không có role này." });

        var actorLevel = await GetActorLevelAsync();
        if (roleEntity.Level >= actorLevel)
            return Forbidden("Bạn chỉ được gỡ role có cấp thấp hơn cấp của mình.");
        if (await _accessLevels.GetUserLevelAsync(user) >= actorLevel)
            return Forbidden("Bạn chỉ được thao tác trên user có cấp thấp hơn cấp của mình.");

        var result = await _userManager.RemoveFromRoleAsync(user, roleEntity.Name!);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return Ok(new { userId, roles = await _userManager.GetRolesAsync(user) });
    }

    // ================= Permissions =================

    [HttpGet("permissions")]
    [HasPermission(AppPermissions.Identity.PermissionsRead)]
    public async Task<IActionResult> GetPermissions()
    {
        var snapshot = await _permissions.GetSnapshotAsync();
        return Ok(new { version = snapshot.Version, items = snapshot.Items });
    }

    [HttpPut("permissions/{code}/level")]
    [HasPermission(AppPermissions.Identity.PermissionsManage)]
    public async Task<IActionResult> UpdatePermissionLevel(string code, [FromBody] UpdatePermissionLevelRequest request)
    {
        var actorLevel = await GetActorLevelAsync();
        var result = await _permissions.UpdateLevelAsync(code, request.Level, actorLevel);

        return result.Status switch
        {
            PermissionUpdateStatus.NotFound => NotFound(new { message = "Không tìm thấy permission." }),
            PermissionUpdateStatus.Forbidden => Forbidden(
                "Bạn chỉ được đổi Level của permission thấp hơn cấp của mình và không được đặt Level cao hơn cấp của mình."),
            _ => Ok(new { code, oldLevel = result.OldLevel, level = request.Level })
        };
    }

    // ================= helpers =================

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>Level hiện tại của người gọi, đọc từ DB. Không xác định được thì trả 0 (không đủ quyền thao tác).</summary>
    private async Task<int> GetActorLevelAsync()
    {
        var id = GetCurrentUserId();
        if (id is null) return 0;

        var user = await _userManager.FindByIdAsync(id.Value.ToString());
        return user is null || !user.IsActive ? 0 : await _accessLevels.GetUserLevelAsync(user);
    }

    private ObjectResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { message });
}
