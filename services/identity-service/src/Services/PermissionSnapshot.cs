using System.Security.Cryptography;
using System.Text;
using IdentityService.DTOs;

namespace IdentityService.Services;

/// <summary>
/// Ảnh chụp bất biến của danh mục permission. <see cref="Version"/> được tính từ (Code, Level) nên
/// giống nhau giữa các instance và đổi khi có Level thay đổi; consumer so sánh để biết cache còn mới không.
/// </summary>
public sealed class PermissionSnapshot
{
    private readonly Dictionary<string, int> _levels;

    public PermissionSnapshot(IEnumerable<PermissionDto> items)
    {
        Items = items
            .OrderBy(i => i.Module, StringComparer.Ordinal)
            .ThenBy(i => i.Code, StringComparer.Ordinal)
            .ToList();
        _levels = Items.ToDictionary(i => i.Code, i => i.Level, StringComparer.Ordinal);
        Version = ComputeVersion(Items);
    }

    public string Version { get; }

    public IReadOnlyList<PermissionDto> Items { get; }

    public int? GetLevel(string code) => _levels.TryGetValue(code, out var level) ? level : null;

    private static string ComputeVersion(IEnumerable<PermissionDto> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items.OrderBy(i => i.Code, StringComparer.Ordinal))
            sb.Append(item.Code).Append(':').Append(item.Level).Append(';');

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16].ToLowerInvariant();
    }
}
