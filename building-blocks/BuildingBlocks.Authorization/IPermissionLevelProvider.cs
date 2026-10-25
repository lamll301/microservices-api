namespace BuildingBlocks.Authorization;

/// <summary>
/// Nguồn cho biết permission yêu cầu Level bao nhiêu. Tách riêng để các service khác dùng lại
/// <see cref="PermissionHandler"/> với cách lấy dữ liệu khác (cache + gọi snapshot của identity-service).
/// </summary>
public interface IPermissionLevelProvider
{
    /// <summary>Trả null nếu permission chưa được khai báo (khi đó luôn từ chối).</summary>
    Task<int?> GetRequiredLevelAsync(string code, CancellationToken ct = default);
}
