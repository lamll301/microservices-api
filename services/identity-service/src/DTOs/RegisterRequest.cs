using System.ComponentModel.DataAnnotations;

namespace IdentityService.DTOs;

public class RegisterRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? FullName { get; set; }
}
