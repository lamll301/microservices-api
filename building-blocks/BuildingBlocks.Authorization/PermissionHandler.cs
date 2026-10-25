using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Authorization;

public class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionLevelProvider _provider;

    public PermissionHandler(IPermissionLevelProvider provider)
    {
        _provider = provider;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        // Từ chối mặc định: thiếu claim, claim không phải số, hoặc permission chưa khai báo thì không Succeed.
        var raw = context.User.FindFirstValue(AuthClaimTypes.Level);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userLevel))
            return;

        var required = await _provider.GetRequiredLevelAsync(requirement.Code);
        if (required is null)
            return;

        if (userLevel >= required.Value)
            context.Succeed(requirement);
    }
}
