using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace IdentityService.Services;

public interface IEmailService
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

public class SmtpSettings
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    public string? User { get; init; }
    public string? Password { get; init; }
    public string FromEmail { get; init; } = "no-reply@identity-service.local";
    public string FromName { get; init; } = "Identity Service";

    public static SmtpSettings FromEnvironment() => new()
    {
        Host = Environment.GetEnvironmentVariable("SMTP_HOST") is { Length: > 0 } h ? h : "localhost",
        Port = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT"), out var p) ? p : 1025,
        User = Environment.GetEnvironmentVariable("SMTP_USER"),
        Password = Environment.GetEnvironmentVariable("SMTP_PASSWORD"),
        FromEmail = Environment.GetEnvironmentVariable("SMTP_FROM_EMAIL") is { Length: > 0 } f ? f : "no-reply@identity-service.local",
        FromName = Environment.GetEnvironmentVariable("SMTP_FROM_NAME") is { Length: > 0 } n ? n : "Identity Service"
    };
}

public class SmtpEmailService : IEmailService
{
    private readonly SmtpSettings _settings;

    public SmtpEmailService(SmtpSettings settings)
    {
        _settings = settings;
    }

    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();

        // Auto: cổng 465 -> SSL, 587 -> STARTTLS, các cổng khác (vd Mailpit 1025) -> STARTTLS nếu server hỗ trợ
        await client.ConnectAsync(_settings.Host, _settings.Port, SecureSocketOptions.Auto, ct);

        if (!string.IsNullOrEmpty(_settings.User))
            await client.AuthenticateAsync(_settings.User, _settings.Password ?? string.Empty, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);
    }
}
