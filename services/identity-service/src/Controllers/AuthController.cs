using System.Net;
using System.Security.Claims;
using IdentityService.Data;
using IdentityService.DTOs;
using IdentityService.Entities;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string GenericEmailMessage =
        "Nếu email tồn tại trong hệ thống, chúng tôi đã gửi hướng dẫn tới hộp thư của bạn.";
    private const string InvalidResetMessage =
        "Liên kết đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IAccessLevelService _accessLevels;
    private readonly IAccountEmailService _accountEmail;
    private readonly AccountSettings _accountSettings;
    private readonly AppDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IAccessLevelService accessLevels,
        IAccountEmailService accountEmail,
        AccountSettings accountSettings,
        AppDbContext db,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _accessLevels = accessLevels;
        _accountEmail = accountEmail;
        _accountSettings = accountSettings;
        _db = db;
        _logger = logger;
    }

    // ================= Đăng ký / đăng nhập =================

    [EnableRateLimiting("email-send")]
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

        await SendConfirmationEmailSafeAsync(user);

        return CreatedAtAction(nameof(Me), new { }, new
        {
            message = "Đăng ký thành công. Vui lòng kiểm tra email để xác thực tài khoản.",
            user = new UserInfo
            {
                Id = user.Id,
                Email = user.Email!,
                FullName = user.FullName,
                Roles = await _userManager.GetRolesAsync(user)
            }
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

        // Chỉ báo "chưa xác thực" SAU khi mật khẩu đúng, tránh lộ thông tin tài khoản cho người lạ
        if (_accountSettings.RequireConfirmedEmail && !user.EmailConfirmed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "email_not_confirmed",
                message = "Email chưa được xác thực. Vui lòng kiểm tra hộp thư hoặc yêu cầu gửi lại email xác thực."
            });
        }

        return Ok(await IssueTokensAsync(user));
    }

    // ================= Xác thực email =================

    /// <summary>Link trong email trỏ vào đây. Trả về một trang HTML đơn giản.</summary>
    [HttpGet("confirm-email")]
    public async Task<IActionResult> ConfirmEmail([FromQuery] Guid userId, [FromQuery] string token)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !TokenCodec.TryDecode(token, out var rawToken))
            return HtmlPage(StatusCodes.Status400BadRequest, "Liên kết không hợp lệ",
                "Liên kết xác thực không hợp lệ.");

        if (user.EmailConfirmed)
            return HtmlPage(StatusCodes.Status200OK, "Email đã được xác thực",
                "Email của bạn đã được xác thực trước đó. Bạn có thể đăng nhập.");

        var result = await _userManager.ConfirmEmailAsync(user, rawToken);
        if (!result.Succeeded)
            return HtmlPage(StatusCodes.Status400BadRequest, "Liên kết đã hết hạn",
                "Liên kết xác thực không hợp lệ hoặc đã hết hạn. Vui lòng yêu cầu gửi lại email xác thực.");

        return HtmlPage(StatusCodes.Status200OK, "Xác thực thành công",
            "Email của bạn đã được xác thực. Bây giờ bạn có thể đăng nhập.");
    }

    [EnableRateLimiting("email-send")]
    [HttpPost("resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation([FromBody] ResendConfirmationRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null && user.IsActive && !user.EmailConfirmed)
            await SendConfirmationEmailSafeAsync(user);

        // Luôn trả cùng một thông báo để không lộ email nào đã đăng ký
        return Ok(new { message = GenericEmailMessage });
    }

    // ================= Quên / đặt lại / đổi mật khẩu =================

    [EnableRateLimiting("email-send")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is not null && user.IsActive)
        {
            try
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                await _accountEmail.SendPasswordResetAsync(user, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gửi email đặt lại mật khẩu cho {UserId} thất bại", user.Id);
            }
        }

        return Ok(new { message = GenericEmailMessage });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive || !TokenCodec.TryDecode(request.Token, out var rawToken))
            return BadRequest(new { message = InvalidResetMessage });

        var result = await _userManager.ResetPasswordAsync(user, rawToken, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
                return BadRequest(new { message = InvalidResetMessage });

            // Lỗi do mật khẩu mới không đủ mạnh
            return BadRequest(new { errors = result.Errors.Select(e => new { e.Code, e.Description }) });
        }

        // Người dùng đã nhận được email nên coi như đã chứng minh sở hữu hộp thư
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        // Mở khóa tài khoản (nếu đang bị khóa do nhập sai) và đăng xuất mọi thiết bị
        await _userManager.SetLockoutEndDateAsync(user, null);
        await _userManager.ResetAccessFailedCountAsync(user);
        await RevokeAllForUserAsync(user.Id);

        return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại." });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null || !user.IsActive) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => new { e.Code, e.Description }) });

        // Đăng xuất các thiết bị khác, cấp cặp token mới cho thiết bị đang dùng
        await RevokeAllForUserAsync(user.Id);
        return Ok(await IssueTokensAsync(user));
    }

    // ================= Refresh token / logout / me =================

    /// <summary>Đổi refresh token lấy cặp access/refresh token mới (refresh token cũ bị thu hồi).</summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var hash = _tokenService.HashToken(request.RefreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);

        if (stored is null)
            return Unauthorized(new { message = "Refresh token không hợp lệ." });

        if (stored.IsRevoked)
        {
            // Chỉ token đã bị XOAY VÒNG (đã đổi lấy token mới) mà vẫn được dùng lại mới là dấu hiệu bị đánh cắp
            // -> thu hồi toàn bộ phiên của user. Token bị thu hồi vì logout / đổi mật khẩu / đặt lại mật khẩu
            // thì chỉ cần từ chối, không được kéo sập các phiên hợp lệ khác (vd phiên vừa cấp sau khi đổi mật khẩu).
            if (stored.ReplacedByTokenHash is not null)
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

    // ================= helpers =================

    private Guid? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }

    /// <summary>Gửi email xác thực; nếu SMTP lỗi thì chỉ ghi log, không làm hỏng request (user có thể bấm gửi lại).</summary>
    private async Task SendConfirmationEmailSafeAsync(ApplicationUser user)
    {
        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            await _accountEmail.SendConfirmationAsync(user, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gửi email xác thực cho {UserId} thất bại", user.Id);
        }
    }

    private async Task<AuthResponse> IssueTokensAsync(ApplicationUser user, RefreshToken? rotateFrom = null)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var level = await _accessLevels.GetLevelAsync(roles);
        var (accessToken, accessExpires) = _tokenService.CreateAccessToken(user, roles, level);
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

    private ContentResult HtmlPage(int statusCode, string title, string message)
    {
        var t = WebUtility.HtmlEncode(title);
        var m = WebUtility.HtmlEncode(message);

        return new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "text/html; charset=utf-8",
            Content = $"""
                <!doctype html>
                <html lang="vi">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <title>{t}</title>
                </head>
                <body style="font-family:Arial,Helvetica,sans-serif;max-width:480px;margin:15vh auto;padding:0 16px;text-align:center;color:#222">
                  <h2>{t}</h2>
                  <p>{m}</p>
                </body>
                </html>
                """
        };
    }
}
