using System.ComponentModel.DataAnnotations;
using IdentityService.Models;

namespace IdentityService.DTOs;

public class PermissionDto
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public int Level { get; set; }
}

public class UpdatePermissionLevelRequest
{
    [Range(AccessLevels.Min, AccessLevels.Max)]
    public int Level { get; set; }
}
