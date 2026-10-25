using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Authorization;
using IdentityService.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Services;

public interface ITokenService
{
    /// <summary>level: Level cao nhất trong các role của user, ghi vào claim "level" để kiểm tra permission.</summary>
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IList<string> roles, int level = 0);

    /// <summary>Tạo refresh token ngẫu nhiên. RawToken trả cho client, TokenHash lưu vào DB.</summary>
    (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreateRefreshToken();

    string HashToken(string rawToken);
}

public class TokenService : ITokenService
{
    private readonly JwtSettings _settings;

    public TokenService(JwtSettings settings)
    {
        _settings = settings;
    }

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IList<string> roles, int level = 0)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("name", user.FullName ?? user.UserName ?? string.Empty),
            new(AuthClaimTypes.Level, level.ToString(CultureInfo.InvariantCulture))
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_settings.ExpireMinutes);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string RawToken, string TokenHash, DateTime ExpiresAtUtc) CreateRefreshToken()
    {
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));
        return (raw, HashToken(raw), DateTime.UtcNow.AddDays(_settings.RefreshExpireDays));
    }

    public string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
