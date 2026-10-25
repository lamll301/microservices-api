using Microsoft.AspNetCore.Authorization;

namespace BuildingBlocks.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string code) => Code = code;

    public string Code { get; }
}
