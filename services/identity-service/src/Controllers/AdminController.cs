using System.Security.Claims;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = AppRoles.Admin)]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly AppDbContext _db;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        AppDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    [HttpGet("users")]
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
    public async Task<IActionResult> GetRoles() =>
        Ok(await _roleManager.Roles.AsNoTracking().Select(r => r.Name).ToListAsync());

    [HttpPost("users/{userId:guid}/roles")]
    public async Task<IActionResult> AssignRole(Guid userId, [FromBody] AssignRoleRequest request)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { message = "Không tìm thấy user." });

        if (!await _roleManager.RoleExistsAsync(request.Role))
            return BadRequest(new { message = $"Role '{request.Role}' không tồn tại." });

        if (await _userManager.IsInRoleAsync(user, request.Role))
            return Conflict(new { message = "User đã có role này." });

        var result = await _userManager.AddToRoleAsync(user, request.Role);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return Ok(new { userId, roles = await _userManager.GetRolesAsync(user) });
    }

    [HttpDelete("users/{userId:guid}/roles/{role}")]
    public async Task<IActionResult> RemoveRole(Guid userId, string role)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (role == AppRoles.Admin && currentUserId == userId.ToString())
            return BadRequest(new { message = "Không thể tự gỡ quyền Admin của chính mình." });

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new { message = "Không tìm thấy user." });

        if (!await _userManager.IsInRoleAsync(user, role))
            return NotFound(new { message = "User không có role này." });

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        return Ok(new { userId, roles = await _userManager.GetRolesAsync(user) });
    }
}
