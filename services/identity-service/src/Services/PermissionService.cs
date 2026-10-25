using BuildingBlocks.Authorization;
using IdentityService.Data;
using IdentityService.DTOs;
using BuildingBlocks.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace IdentityService.Services;

public enum PermissionUpdateStatus { Updated, Unchanged, NotFound, Forbidden }

public sealed record PermissionUpdateResult(PermissionUpdateStatus Status, int OldLevel = 0);

public interface IPermissionService : IPermissionLevelProvider
{
    Task<PermissionSnapshot> GetSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Đổi Level của permission. Người đổi chỉ được sửa permission có Level hiện tại THẤP HƠN cấp của mình
    /// và không được đặt Level cao hơn cấp của mình (chống leo thang đặc quyền).
    /// </summary>
    Task<PermissionUpdateResult> UpdateLevelAsync(string code, int newLevel, int actorLevel, CancellationToken ct = default);

    /// <summary>Xóa cache ngay (dùng khi nhận tín hiệu thay đổi).</summary>
    void Invalidate();
}

/// <summary>
/// Cache + event-driven: snapshot lưu trong bộ nhớ với TTL ngắn (lưới an toàn nếu mất event);
/// khi Level đổi thì xóa cache cục bộ ngay và phát <see cref="PermissionLevelChangedEvent"/> cho các service khác.
/// </summary>
public class PermissionService : IPermissionService
{
    private const string CacheKey = "permissions:snapshot";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IEventPublisher _events;
    private readonly ILogger<PermissionService> _logger;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    public PermissionService(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        IEventPublisher events,
        ILogger<PermissionService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _events = events;
        _logger = logger;
    }

    public async Task<int?> GetRequiredLevelAsync(string code, CancellationToken ct = default) =>
        (await GetSnapshotAsync(ct)).GetLevel(code);

    public async Task<PermissionSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out PermissionSnapshot? cached) && cached is not null)
            return cached;

        // Một luồng nạp, các luồng khác chờ rồi dùng kết quả (tránh dồn truy vấn DB khi cache vừa hết hạn).
        await _loadLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(CacheKey, out cached) && cached is not null)
                return cached;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var items = await db.Permissions
                .AsNoTracking()
                .Select(p => new PermissionDto
                {
                    Code = p.Code,
                    Description = p.Description,
                    Module = p.Module,
                    Level = p.Level
                })
                .ToListAsync(ct);

            var snapshot = new PermissionSnapshot(items);
            _cache.Set(CacheKey, snapshot, CacheTtl);
            return snapshot;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<PermissionUpdateResult> UpdateLevelAsync(
        string code, int newLevel, int actorLevel, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (permission is null)
            return new PermissionUpdateResult(PermissionUpdateStatus.NotFound);

        var oldLevel = permission.Level;
        if (oldLevel >= actorLevel || newLevel > actorLevel)
            return new PermissionUpdateResult(PermissionUpdateStatus.Forbidden, oldLevel);

        if (oldLevel == newLevel)
            return new PermissionUpdateResult(PermissionUpdateStatus.Unchanged, oldLevel);

        permission.Level = newLevel;
        permission.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        Invalidate();

        // DB đã commit: lỗi phát event không được làm hỏng request; các service khác sẽ tự làm mới theo TTL.
        try
        {
            var snapshot = await GetSnapshotAsync(ct);
            await _events.PublishAsync(
                new PermissionLevelChangedEvent(code, oldLevel, newLevel, snapshot.Version, DateTime.UtcNow), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Phát event đổi Level của permission {Code} thất bại", code);
        }

        return new PermissionUpdateResult(PermissionUpdateStatus.Updated, oldLevel);
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}
