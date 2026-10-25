using Microsoft.AspNetCore.Identity;

namespace IdentityService.Models;

public class ApplicationRole : IdentityRole<Guid>
{
    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }

    /// <summary>Cấp bậc của role (xem <see cref="AccessLevels"/>). Level của user = Level cao nhất trong các role.</summary>
    public int Level { get; set; }
}
