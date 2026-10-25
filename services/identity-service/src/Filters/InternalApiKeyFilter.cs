using System.Security.Cryptography;
using System.Text;
using IdentityService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IdentityService.Filters;

/// <summary>Bảo vệ endpoint nội bộ (service-to-service) bằng header X-Internal-Key. Dùng: [ServiceFilter(typeof(InternalApiKeyFilter))].</summary>
public class InternalApiKeyFilter : IAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Key";

    private readonly InternalApiSettings _settings;

    public InternalApiKeyFilter(InternalApiSettings settings)
    {
        _settings = settings;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Chưa cấu hình khóa (hoặc quá ngắn) thì tắt hẳn endpoint, không cho truy cập không khóa.
        if (!_settings.Enabled)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!FixedTimeEquals(provided, _settings.Key!))
            context.Result = new UnauthorizedObjectResult(new { message = "Khóa nội bộ không hợp lệ." });
    }

    // So sánh hash để thời gian không phụ thuộc độ dài / vị trí ký tự sai.
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
