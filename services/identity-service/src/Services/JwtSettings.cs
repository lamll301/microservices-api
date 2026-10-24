namespace IdentityService.Services;

public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int ExpireMinutes { get; set; } = 60;
    public int RefreshExpireDays { get; set; } = 7;

    public static JwtSettings FromEnvironment()
    {
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
            throw new InvalidOperationException("JWT_SECRET phải có ít nhất 32 ký tự.");

        return new JwtSettings
        {
            Secret = secret,
            Issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "identity-service",
            Audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "microservices-api",
            ExpireMinutes = int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRE_MINUTES"), out var m) ? m : 60,
            RefreshExpireDays = int.TryParse(Environment.GetEnvironmentVariable("JWT_REFRESH_EXPIRE_DAYS"), out var d) ? d : 7
        };
    }
}
