using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Web;
using IdentityService.DTOs;

namespace IdentityService.Tests.Infrastructure;

/// <summary>Kết quả của POST /api/auth/register.</summary>
public record RegisterResult(string Message, UserInfo User);

public static class HttpExtensions
{
    /// <summary>Kiểm tra status code; nếu sai thì in luôn body trả về để biết API báo lỗi gì.</summary>
    public static async Task ShouldBeAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected) return;

        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Mong đợi {(int)expected} {expected} nhưng nhận {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>())!;

    public static Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    /// <summary>Đăng nhập và yêu cầu phải thành công (200).</summary>
    public static async Task<AuthResponse> LoginOkAsync(
        this HttpClient client, string email, string password = IdentityApiFactory.DefaultPassword)
    {
        var response = await client.LoginAsync(email, password);
        await response.ShouldBeAsync(HttpStatusCode.OK);
        return await response.ReadAsAsync<AuthResponse>();
    }

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
}

public static class EmailLinks
{
    private static readonly Regex Href = new("href=\"([^\"]+)\"", RegexOptions.Compiled);

    /// <summary>Lấy link đầu tiên (nút bấm) trong email. Email HTML-encode nên phải decode lại (&amp; -> &).</summary>
    public static Uri FirstLink(SentEmail email)
    {
        var match = Href.Match(email.HtmlBody);
        Assert.True(match.Success, "Không tìm thấy link nào trong email.");
        return new Uri(WebUtility.HtmlDecode(match.Groups[1].Value));
    }

    /// <summary>Đọc một tham số trên query string (đã URL-decode).</summary>
    public static string QueryValue(this Uri uri, string name)
    {
        var value = HttpUtility.ParseQueryString(uri.Query)[name];
        Assert.False(string.IsNullOrEmpty(value), $"Link không có tham số '{name}': {uri}");
        return value!;
    }
}
