namespace IdentityService.Entities;

/// <summary>
/// Phòng ban. Bổ sung cho mô hình Level (một trục thẳng) khi cần phân quyền theo lĩnh vực.
/// Hiện mới chỉ có bảng, chưa gắn với user/permission.
/// </summary>
public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
