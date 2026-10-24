using System.ComponentModel.DataAnnotations;

namespace IdentityService.DTOs;

public class AssignRoleRequest
{
    [Required]
    public string Role { get; set; } = string.Empty;
}

public class AdminUserDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public IList<string> Roles { get; set; } = new List<string>();
}
