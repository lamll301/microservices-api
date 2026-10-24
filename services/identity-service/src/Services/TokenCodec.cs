using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace IdentityService.Services;

/// <summary>
/// Token của Identity chứa ký tự như + / = nên không an toàn khi đưa vào URL.
/// Mã hóa Base64Url trước khi gửi trong link, giải mã khi nhận lại.
/// </summary>
public static class TokenCodec
{
    public static string Encode(string token) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static bool TryDecode(string? encoded, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(encoded)) return false;

        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
