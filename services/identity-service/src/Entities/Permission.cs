namespace IdentityService.Entities;

/// <summary>Một permission và Level tối thiểu cần có để thực hiện. User được phép khi Level của user >= Level này.</summary>
public class Permission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã duy nhất, dạng {service}.{resource}.{action}, ví dụ identity.users.read.</summary>
    public string Code { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Nhóm theo service, dùng để hiển thị ở UI quản trị.</summary>
    public string Module { get; set; } = string.Empty;

    public int Level { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
