namespace BuildingBlocks.Events;

/// <summary>
/// Level của một permission vừa đổi (identity-service phát, các service khác nhận).
/// Chỉ là tín hiệu làm mất hiệu lực cache: consumer nhận được thì xóa cache và tải lại snapshot
/// (so sánh CatalogVersion), không tin nội dung event để quyết định quyền.
/// </summary>
public sealed record PermissionLevelChangedEvent(
    string Code,
    int OldLevel,
    int NewLevel,
    string CatalogVersion,
    DateTime OccurredAtUtc);
