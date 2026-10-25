using System.Collections.Concurrent;
using IdentityService.Services;

namespace IdentityService.Tests.Infrastructure;

public record SentEmail(string To, string Subject, string HtmlBody);

/// <summary>
/// Thay cho SmtpEmailService trong test: không gửi mail thật, chỉ ghi lại để test đọc link xác thực / đặt lại mật khẩu.
/// </summary>
public class FakeEmailService : IEmailService
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        _sent.Enqueue(new SentEmail(toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }

    /// <summary>Tất cả email đã gửi tới địa chỉ này (cũ -> mới).</summary>
    public IReadOnlyList<SentEmail> To(string email) =>
        _sent.Where(e => string.Equals(e.To, email, StringComparison.OrdinalIgnoreCase)).ToList();

    public SentEmail? LastTo(string email) => To(email).LastOrDefault();
}
