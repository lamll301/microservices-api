using System.Security.Claims;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Entities;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly AppDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        AppDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _db = db;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
            return Conflict(new { message = "Email đã được sử dụng." });

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email,
            Email = request.Email,
            FullName = request.FullName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => new { e.Code, e.Description }) });

        // Role mặc định cho user mới (role được seed khi service khởi động)
        await _userManager.AddToRoleAsync(user, AppRoles.User);

        return CreatedAtAction(nameof(Me), new { }, new UserInfo
        {
            Id = user.Id,
            Email = user.Email!,
            FullName = user.FullName,
            Roles = await _userManager.GetRolesAsync(user)
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });

        // lockoutOnFailure: true -> khóa tạm thời nếu nhập sai nhiều lần
        var check = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked, new { message = "Tài khoản tạm thời bị khóa, thử lại sau." });
        if (!check.Succeeded)
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });

        return Ok(await IssueTokensAsync(user));
    }

    /// <summary>Đổi refresh token lấy cặp access/refresh token mới (refresh token cũ bị thu hồi).</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var hash = _tokenService.HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null)
            return Unauthorized(new { message = "Refresh token không hợp lệ." });

        // Token đã bị thu hồi mà vẫn được dùng lại -> nghi ngờ bị đánh cắp, thu hồi toàn bộ token của user
        if (stored.IsRevoked)
        {
            await RevokeAllForUserAsync(stored.UserId);
            return Unauthorized(new { message = "Refresh token đã bị thu hồi. Vui lòng đăng nhập lại." });
        }

        if (stored.IsExpired)
            return Unauthorized(new { message = "Refresh token đã hết hạn. Vui lòng đăng nhập lại." });

        var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive)
            return Unauthorized(new { message = "Tài khoản không khả dụng." });

        return Ok(await IssueTokensAsync(user, rotateFrom: stored));
    }

    /// <summary>Đăng xuất thiết bị hiện tại: thu hồi refresh token được gửi lên.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        var hash = _tokenService.HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is { IsRevoked: false })
        {
            stored.RevokedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    /// <summary>Đăng xuất mọi thiết bị.</summary>
    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await RevokeAllForUserAsync(userId.Value);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null) return Unauthorized();

        return Ok(new UserInfo
        {
            Id = user.Id,
            Email = user.Email!,
            FullName = user.FullName,
            Roles = await _userManager.GetRolesAsync(user)
        });
    }

    // ---------- helpers ----------

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, RefreshToken? rotateFrom = null)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, accessExpires) = _tokenService.CreateAccessToken(user, roles);
        var (rawRefresh, refreshHash, refreshExpires) = _tokenService.CreateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = refreshExpires,
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        if (rotateFrom is not null)
        {
            rotateFrom.RevokedAtUtc = DateTime.UtcNow;
            rotateFrom.ReplacedByTokenHash = refreshHash;
        }

        await _db.SaveChangesAsync();

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresAtUtc = accessExpires,
            RefreshToken = rawRefresh,
            RefreshTokenExpiresAtUtc = refreshExpires,
            User = new UserInfo
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Roles = roles
            }
        };
    }

    private Task RevokeAllForUserAsync(Guid userId)
    {
        var now = (DateTime?)DateTime.UtcNow;
        return _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now));
    }
}
