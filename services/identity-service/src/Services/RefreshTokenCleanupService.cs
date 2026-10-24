using IdentityService.Data;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

/// <summary>
/// Định kỳ xóa các refresh token đã hết hạn quá lâu khỏi DB.
/// Token đã bị thu hồi nhưng CHƯA hết hạn vẫn được giữ lại để phát hiện việc dùng lại token cũ.
/// </summary>
public class RefreshTokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RefreshTokenCleanupService> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retention;

    public RefreshTokenCleanupService(IServiceScopeFactory scopeFactory, ILogger<RefreshTokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var intervalHours = ReadPositiveInt("TOKEN_CLEANUP_INTERVAL_HOURS", 6);
        var retentionDays = ReadPositiveInt("TOKEN_CLEANUP_RETENTION_DAYS", 7);
        _interval = TimeSpan.FromHours(intervalHours);
        _retention = TimeSpan.FromDays(retentionDays);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("TOKEN_CLEANUP_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Refresh token cleanup đã bị tắt (TOKEN_CLEANUP_ENABLED=false).");
            return;
        }

        _logger.LogInformation(
            "Refresh token cleanup chạy mỗi {Interval}, giữ token đã hết hạn thêm {Retention}.",
            _interval, _retention);

        // Chờ một chút để app (và migration) khởi động xong rồi mới dọn lần đầu
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(_interval);
        do
        {
            await CleanupAsync(stoppingToken);
        }
        while (await WaitNextTickAsync(timer, stoppingToken));
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var cutoff = DateTime.UtcNow - _retention;
            var deleted = await db.RefreshTokens
                .Where(t => t.ExpiresAtUtc < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
                _logger.LogInformation("Đã xóa {Count} refresh token hết hạn.", deleted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // đang tắt app
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dọn refresh token thất bại, sẽ thử lại ở lần sau.");
        }
    }

    private static async Task<bool> WaitNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private static int ReadPositiveInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;
}
