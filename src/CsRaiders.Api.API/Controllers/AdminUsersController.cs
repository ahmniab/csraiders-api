using System.Security.Claims;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.Application.Features.Admin;
using CsRaiders.Api.Application.Features.Admin.Users;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.API.Controllers;

[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(IMediator mediator) : ControllerBase
{
    [Authorize(Policy = PermissionCatalog.UserWrite)]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    [HttpPost("{userId:guid}/permissions")]
    public async Task<IActionResult> SetPermission(Guid userId, SetUserPermissionOverrideRequest request, CancellationToken ct)
    {
        PermissionEffect? effect = request.Clear ? null : request.Effect?.ToLowerInvariant() switch
        {
            "grant" => PermissionEffect.Grant,
            "deny" => PermissionEffect.Deny,
            _ => null
        };
        if (!request.Clear && effect is null)
            return UnprocessableEntity(new { error = new { code = "validation_failed", message = "effect must be grant or deny." } });
        return ToActionResult(await mediator.Send(new SetUserPermissionOverrideCommand(UserId(), userId, request.Permission, effect), ct));
    }

    [Authorize(Policy = PermissionCatalog.UserWrite)]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    [HttpPut("{userId:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid userId, ElevateUserRolesRequest request, CancellationToken ct) =>
        ToActionResult(await mediator.Send(new ElevateUserRolesCommand(UserId(), userId, request.RoleIds ?? []), ct));

    [Authorize(Policy = PermissionCatalog.UserWrite)]
    [HttpPost("{userId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid userId, CancellationToken ct)
    {
        var result = await mediator.Send(new SuspendUserCommand(UserId(), userId), ct);
        return result.Succeeded ? Ok(new { suspended = true }) : result.NotFound ? NotFound() : Conflict(new { error = new { code = "invalid_state", message = "The user cannot be suspended in its current state." } });
    }

    [Authorize(Policy = PermissionCatalog.UserWrite)]
    [HttpPost("{userId:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken ct)
    {
        var result = await mediator.Send(new ReactivateUserCommand(UserId(), userId), ct);
        return result.Succeeded ? Ok(new { reactivated = true }) : result.NotFound ? NotFound() : Conflict(new { error = new { code = "invalid_state", message = "The user is not suspended." } });
    }

    private Guid UserId() => Guid.Parse(User.FindFirstValue("sub")!);
    private IActionResult ToActionResult(AdminChangeResult result) => result.Succeeded ? Ok(new { updated = true })
        : result.NotFound ? NotFound()
        : StatusCode(StatusCodes.Status403Forbidden, new { error = new { code = "forbidden", message = "The requested permission change is not allowed." } });
}

public sealed record SetUserPermissionOverrideRequest(string Permission, string? Effect, bool Clear = false);
public sealed record ElevateUserRolesRequest(IReadOnlyCollection<Guid>? RoleIds);
