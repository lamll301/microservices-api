using IdentityService.Filters;
using IdentityService.Services;
using Microsoft.AspNetCore.Mvc;

namespace IdentityService.Controllers;

/// <summary>
/// Endpoint service-to-service (xác thực bằng X-Internal-Key), KHÔNG công khai ra ngoài gateway.
/// Các service khác gọi để nạp snapshot Level của permission khi khởi động và khi nhận event thay đổi.
/// </summary>
[ApiController]
[Route("api/internal")]
[ServiceFilter(typeof(InternalApiKeyFilter))]
public class InternalController : ControllerBase
{
    private readonly IPermissionService _permissions;

    public InternalController(IPermissionService permissions)
    {
        _permissions = permissions;
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions()
    {
        var snapshot = await _permissions.GetSnapshotAsync();
        return Ok(new { version = snapshot.Version, items = snapshot.Items });
    }
}
