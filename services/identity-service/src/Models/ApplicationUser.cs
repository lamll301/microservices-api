
using Microsoft.AspNetCore.Identity;

namespace IdentityService.Models;

public class ApplicationUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
