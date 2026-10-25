using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IdentityService.Models;
using IdentityService.Services;
using Microsoft.IdentityModel.Tokens;

namespace IdentityService.Tests.Unit;

public class TokenServiceTests
{
    private static readonly JwtSettings Settings = new()
    {
        Secret = "unit-test-secret-key-at-least-32-characters-long!",
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpireMinutes = 15,
        RefreshExpireDays = 7
    };

    private readonly TokenService _sut = new(Settings);

    private static ApplicationUser NewUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "a@test.local",
        Email = "a@test.local",
        FullName = "Test User"
    };

    private static TokenValidationParameters ValidationFor(string secret) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Settings.Issuer,
        ValidateAudience = true,
        ValidAudience = Settings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ValidateLifetime = true
    };

    // ---------- access token ----------

    [Fact]
    public void CreateAccessToken_contains_identity_and_role_claims()
    {
        var user = NewUser();

        var (token, expiresAtUtc) = _sut.CreateAccessToken(user, new List<string> { "Admin", "User" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(Settings.Issuer, jwt.Issuer);
        Assert.Contains(Settings.Audience, jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Email && c.Value == user.Email);

        var roles = jwt.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .Select(c => c.Value)
            .OrderBy(r => r)
            .ToList();
        Assert.Equal(new[] { "Admin", "User" }, roles);

        Assert.InRange(expiresAtUtc, DateTime.UtcNow.AddMinutes(14), DateTime.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void CreateAccessToken_is_signed_with_the_configured_secret()
    {
        var (token, _) = _sut.CreateAccessToken(NewUser(), new List<string>());
        var handler = new JwtSecurityTokenHandler();

        // Đúng secret -> hợp lệ
        handler.ValidateToken(token, ValidationFor(Settings.Secret), out var validated);
        Assert.NotNull(validated);

        // Sai secret -> bị từ chối
        Assert.ThrowsAny<SecurityTokenException>(() =>
            handler.ValidateToken(token, ValidationFor("a-completely-different-secret-key-32-chars!!"), out _));
    }

    [Fact]
    public void CreateAccessToken_gives_each_token_a_unique_id()
    {
        var user = NewUser();

        var (first, _) = _sut.CreateAccessToken(user, new List<string>());
        var (second, _) = _sut.CreateAccessToken(user, new List<string>());

        Assert.NotEqual(first, second);
    }

    // ---------- refresh token ----------

    [Fact]
    public void CreateRefreshToken_returns_raw_token_hash_and_expiry()
    {
        var (raw, hash, expiresAtUtc) = _sut.CreateRefreshToken();

        Assert.False(string.IsNullOrWhiteSpace(raw));
        Assert.NotEqual(raw, hash);                    // DB chỉ lưu hash, không lưu token gốc
        Assert.Equal(_sut.HashToken(raw), hash);
        Assert.InRange(expiresAtUtc, DateTime.UtcNow.AddDays(6.9), DateTime.UtcNow.AddDays(7.1));
    }

    [Fact]
    public void CreateRefreshToken_is_random()
    {
        var (first, _, _) = _sut.CreateRefreshToken();
        var (second, _, _) = _sut.CreateRefreshToken();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void HashToken_is_deterministic_and_hex_encoded()
    {
        var a = _sut.HashToken("same-input");
        var b = _sut.HashToken("same-input");
        var c = _sut.HashToken("other-input");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);                    // SHA-256 = 32 byte = 64 ký tự hex
        Assert.Matches("^[0-9A-F]+$", a);
    }
}
