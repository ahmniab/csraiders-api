using System.Security.Claims;
using CsRaiders.Api.Application.Features.Admin;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.API.Controllers;

[ApiController]
[Route("api/v1/admin/roles")]
public sealed class AdminRolesController(IMediator mediator, IRoleRepository roles) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await roles.ListAsync(ct);
        return Ok(result
            .Select(role => new
            {
                id = role.Id, 
                name = role.Name, 
                slug = role.Slug, 
                scope = role.Scope.ToString(), 
                isSystem = role.IsSystem, 
                permissions = role.Permissions
            }));
    }

    [HttpGet("permissions")]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public IActionResult Permissions() 
        => Ok(new { permissions = PermissionCatalog.All });

    [HttpGet("{roleId:guid}")]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public async Task<IActionResult> Get(Guid roleId, CancellationToken ct)
    {
        var role = await roles.FindByIdAsync(roleId, ct: ct);
        return role is null ? 
            NotFound() 
            : Ok(new
            {
                id = role.Id, 
                name = role.Name, 
                slug = role.Slug, 
                scope = role.Scope.ToString(), 
                isSystem = role.IsSystem, 
                permissions = role.Permissions
            });
    }

    [HttpPost]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<RoleScope>(request.Scope, true, out var scope))
            return UnprocessableEntity(new { error = new { code = "validation_failed", message = "scope must be student or staff." } });
        var result = await mediator.Send(new CreateRoleCommand(UserId(), request.Name, request.Slug, scope, request.Permissions ?? []), ct);
        if (result.Conflict) 
            return Conflict(new
            {
                error = new
                {
                    code = "slug_conflict", 
                    message = "A role with this slug already exists."
                }
            });
        if (result.Forbidden) 
            return ForbiddenResult();
        return Created($"/api/v1/admin/roles/{result.RoleId}", new { id = result.RoleId });
    }

    [HttpPut("{roleId:guid}/permissions")]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public async Task<IActionResult> UpdatePermissions(Guid roleId, UpdateRolePermissionsRequest request, CancellationToken ct)
    {
        var result = await mediator
            .Send(new UpdateRolePermissionsCommand(
                UserId(), 
                roleId, 
                request.Permissions ?? []), 
                ct);
        return ToActionResult(result);
    }

    [HttpPut("{roleId:guid}")]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public async Task<IActionResult> Update(Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<RoleScope>(request.Scope, true, out var scope))
            return UnprocessableEntity(new
            {
                error = new
                {
                    code = "validation_failed", 
                    message = "scope must be student or staff."
                }
            });
        return ToActionResult(await mediator.Send(new UpdateRoleCommand(UserId(), roleId, request.Name, request.Slug, scope, request.Permissions ?? []), ct));
    }

    [HttpDelete("{roleId:guid}")]
    [Authorize(Policy = PermissionCatalog.RoleManage)]
    public async Task<IActionResult> Delete(Guid roleId, CancellationToken ct) =>
        ToActionResult(await mediator
            .Send(new DeleteRoleCommand(UserId(), roleId), ct));

    private Guid UserId() 
        => Guid.Parse(User.FindFirstValue("sub")!);
    private IActionResult ToActionResult(AdminChangeResult result) 
        => result.Succeeded ? Ok(new { updated = true })
        : result.NotFound ? NotFound()
        : StatusCode(StatusCodes.Status403Forbidden, 
            new
            {
                error = new
                {
                    code = "forbidden", 
                    message = "The requested permission change is not allowed."
                }
            });
    private IActionResult ForbiddenResult() 
        => StatusCode(StatusCodes.Status403Forbidden, new
        {
            error = new
            {
                code = "forbidden", 
                message = "The requested permission change is not allowed."
            }
        });
    
}

public sealed record UpdateRolePermissionsRequest(
    IReadOnlyCollection<string>? Permissions);
public sealed record CreateRoleRequest(
    string Name, 
    string Slug, 
    string Scope, 
    IReadOnlyCollection<string>? Permissions);
public sealed record UpdateRoleRequest(
    string Name, 
    string Slug, 
    string Scope, 
    IReadOnlyCollection<string>? Permissions);
