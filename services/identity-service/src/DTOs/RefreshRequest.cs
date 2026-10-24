using System.ComponentModel.DataAnnotations;

namespace IdentityService.DTOs;

public class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
