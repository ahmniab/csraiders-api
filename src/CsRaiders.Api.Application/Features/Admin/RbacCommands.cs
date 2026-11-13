using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using Mediator;

namespace CsRaiders.Api.Application.Features.Admin;

public sealed record UpdateRolePermissionsCommand(Guid GrantorUserId, Guid RoleId, IReadOnlyCollection<string> Permissions)
    : IRequest<AdminChangeResult>;
public sealed record SetUserPermissionOverrideCommand(Guid GrantorUserId, Guid UserId, string Permission, PermissionEffect? Effect)
    : IRequest<AdminChangeResult>;
public sealed record ElevateUserRolesCommand(Guid GrantorUserId, Guid UserId, IReadOnlyCollection<Guid> RoleIds)
    : IRequest<AdminChangeResult>;
public sealed record CreateRoleCommand(Guid GrantorUserId, string Name, string Slug, RoleScope Scope, IReadOnlyCollection<string> Permissions)
    : IRequest<RoleChangeResult>;
public sealed record UpdateRoleCommand(Guid GrantorUserId, Guid RoleId, string Name, string Slug, RoleScope Scope, IReadOnlyCollection<string> Permissions)
    : IRequest<AdminChangeResult>;
public sealed record DeleteRoleCommand(Guid GrantorUserId, Guid RoleId) : IRequest<AdminChangeResult>;

public sealed record AdminChangeResult(bool Succeeded, bool NotFound = false, bool Forbidden = false)
{
    public static AdminChangeResult Missing() => new(false, NotFound: true);
    public static AdminChangeResult Denied() => new(false, Forbidden: true);
}
public sealed record RoleChangeResult(bool Succeeded, Guid? RoleId = null, bool Conflict = false, bool Forbidden = false)
{
    public static RoleChangeResult Denied() => new(false, Forbidden: true);
}

public static class RbacCommandSupport
{
    public static async Task<IReadOnlySet<string>?> EffectiveAsync(
        Guid userId, IUserRepository users, IRoleRepository roles, CancellationToken ct)
    {
        var user = await users.FindByIdWithPermissionsAsync(userId, ct: ct);
        if (user is null || !user.CanAuthenticate) return null;
        var assigned = await roles.FindByIdsAsync(user.Roles.Select(x => x.RoleId), ct);
        return EffectivePermissions.Resolve(assigned, user.PermissionOverrides);
    }

    public static bool CanGrant(IReadOnlySet<string>? grantorPermissions, IEnumerable<string> permissions) =>
        grantorPermissions is not null && permissions.All(grantorPermissions.Contains);

    public static bool HasLostSensitivePermission(
        IReadOnlySet<string> permissionsBefore,
        IReadOnlySet<string> permissionsAfter)
    {
        foreach (var sensitive in SensitivePermissions.All)
        {
            if (permissionsBefore.Contains(sensitive) && !permissionsAfter.Contains(sensitive))
            {
                return true;
            }
        }
        return false;
    }

    public static async Task RevokeUserSessionsAsync(
        Guid userId,
        ISessionRepository sessions,
        ISessionCacheHook sessionCache,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var active = await sessions.FindActiveByUserIdAsync(userId, now, ct: ct);
        await sessions.RevokeAllByUserIdAsync(userId, SessionRevokeReason.AdminRevoked, now, ct);
        foreach (var session in active)
        {
            sessionCache.Evict(session.Id);
        }
    }
}

public sealed class UpdateRolePermissionsCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ISessionRepository sessions,
    ISessionCacheHook sessionCache,
    ISessionReconciliationService reconciliation,
    IRbacInvalidationToken invalidation,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<UpdateRolePermissionsCommand, AdminChangeResult>
{
    public async ValueTask<AdminChangeResult> Handle(UpdateRolePermissionsCommand request, CancellationToken ct)
    {
        var grantor = await RbacCommandSupport.EffectiveAsync(request.GrantorUserId, users, roles, ct);
        if (!RbacCommandSupport.CanGrant(grantor, request.Permissions)) return AdminChangeResult.Denied();

        var role = await roles.FindByIdAsync(request.RoleId, track: true, ct);
        if (role is null) return AdminChangeResult.Missing();
        var oldPermissions = role.Permissions.ToArray();
        var change = role.SetPermissions(request.Permissions);
        roles.Update(role);

        var sensitiveRemoved = change.Removed.Any(SensitivePermissions.IsSensitive);
        var multiRemoved = change.Removed.Contains(PermissionCatalog.SessionMulti);

        if (sensitiveRemoved || multiRemoved)
        {
            var oldRole = Role.Create(role.Name, role.Slug, role.Scope, oldPermissions, role.IsSystem);
            var now = clock.UtcNow;
            foreach (var userId in await users.FindIdsByRoleIdAsync(role.Id, ct))
            {
                var user = await users.FindByIdWithPermissionsAsync(userId, ct: ct);
                if (user is null) continue;
                var otherRoles = (await roles.FindByIdsAsync(user.Roles.Select(x => x.RoleId), ct))
                    .Where(x => x.Id != role.Id).ToList();

                var effectiveBefore = EffectivePermissions.Resolve(otherRoles.Append(oldRole), user.PermissionOverrides);
                var effectiveAfter = EffectivePermissions.Resolve(otherRoles.Append(role), user.PermissionOverrides);

                if (sensitiveRemoved && RbacCommandSupport.HasLostSensitivePermission(effectiveBefore, effectiveAfter))
                {
                    await RbacCommandSupport.RevokeUserSessionsAsync(userId, sessions, sessionCache, now, ct);
                }

                if (multiRemoved &&
                    effectiveBefore.Contains(PermissionCatalog.SessionMulti) &&
                    !effectiveAfter.Contains(PermissionCatalog.SessionMulti))
                {
                    await reconciliation.ReconcileAfterMultiSessionLossAsync(userId, ct);
                }
            }
        }

        invalidation.Invalidate();
        await audit.WriteAsync("role_permissions_updated", request.GrantorUserId,
            details: new { roleId = role.Id, permissions = request.Permissions }, ct: ct);
        return new(true);
    }
}

public sealed class CreateRoleCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IRbacInvalidationToken invalidation,
    ISecurityAuditWriter audit) : IRequestHandler<CreateRoleCommand, RoleChangeResult>
{
    public async ValueTask<RoleChangeResult> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var grantor = await RbacCommandSupport.EffectiveAsync(request.GrantorUserId, users, roles, ct);
        if (!RbacCommandSupport.CanGrant(grantor, request.Permissions)) return RoleChangeResult.Denied();
        if (await roles.FindBySlugAsync(request.Slug, ct: ct) is not null) return new(false, Conflict: true);
        var role = Role.Create(request.Name, request.Slug, request.Scope, request.Permissions);
        await roles.AddAsync(role, ct);
        invalidation.Invalidate();
        await audit.WriteAsync("role_created", request.GrantorUserId, details: new { roleId = role.Id, slug = role.Slug }, ct: ct);
        return new(true, role.Id);
    }
}

public sealed class UpdateRoleCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ISessionRepository sessions,
    ISessionCacheHook sessionCache,
    ISessionReconciliationService reconciliation,
    IRbacInvalidationToken invalidation,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<UpdateRoleCommand, AdminChangeResult>
{
    public async ValueTask<AdminChangeResult> Handle(UpdateRoleCommand request, CancellationToken ct)
    {
        var grantor = await RbacCommandSupport.EffectiveAsync(request.GrantorUserId, users, roles, ct);
        if (!RbacCommandSupport.CanGrant(grantor, request.Permissions)) return AdminChangeResult.Denied();
        var role = await roles.FindByIdAsync(request.RoleId, track: true, ct);
        if (role is null) return AdminChangeResult.Missing();
        if (role.IsSystem) return AdminChangeResult.Denied();
        var existing = await roles.FindBySlugAsync(request.Slug, ct: ct);
        if (existing is not null && existing.Id != role.Id) return AdminChangeResult.Denied();

        var oldPermissions = role.Permissions.ToArray();
        var change = role.SetPermissions(request.Permissions);
        role.UpdateDetails(request.Name, request.Slug, request.Scope);
        roles.Update(role);

        var sensitiveRemoved = change.Removed.Any(SensitivePermissions.IsSensitive);
        var multiRemoved = change.Removed.Contains(PermissionCatalog.SessionMulti);

        if (sensitiveRemoved || multiRemoved)
        {
            var oldRole = Role.Create(role.Name, role.Slug, role.Scope, oldPermissions, role.IsSystem);
            var now = clock.UtcNow;
            foreach (var userId in await users.FindIdsByRoleIdAsync(role.Id, ct))
            {
                var user = await users.FindByIdWithPermissionsAsync(userId, ct: ct);
                if (user is null) continue;
                var otherRoles = (await roles.FindByIdsAsync(user.Roles.Select(x => x.RoleId), ct))
                    .Where(x => x.Id != role.Id).ToList();

                var effectiveBefore = EffectivePermissions.Resolve(otherRoles.Append(oldRole), user.PermissionOverrides);
                var effectiveAfter = EffectivePermissions.Resolve(otherRoles.Append(role), user.PermissionOverrides);

                if (sensitiveRemoved && RbacCommandSupport.HasLostSensitivePermission(effectiveBefore, effectiveAfter))
                {
                    await RbacCommandSupport.RevokeUserSessionsAsync(userId, sessions, sessionCache, now, ct);
                }

                if (multiRemoved &&
                    effectiveBefore.Contains(PermissionCatalog.SessionMulti) &&
                    !effectiveAfter.Contains(PermissionCatalog.SessionMulti))
                {
                    await reconciliation.ReconcileAfterMultiSessionLossAsync(userId, ct);
                }
            }
        }

        invalidation.Invalidate();
        await audit.WriteAsync("role_updated", request.GrantorUserId, details: new { roleId = role.Id, slug = role.Slug }, ct: ct);
        return new(true);
    }
}

public sealed class DeleteRoleCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ISessionRepository sessions,
    ISessionCacheHook sessionCache,
    IRbacInvalidationToken invalidation,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<DeleteRoleCommand, AdminChangeResult>
{
    public async ValueTask<AdminChangeResult> Handle(DeleteRoleCommand request, CancellationToken ct)
    {
        var grantor = await RbacCommandSupport.EffectiveAsync(request.GrantorUserId, users, roles, ct);
        if (!RbacCommandSupport.CanGrant(grantor, [PermissionCatalog.RoleManage])) return AdminChangeResult.Denied();
        var role = await roles.FindByIdAsync(request.RoleId, track: true, ct);
        if (role is null) return AdminChangeResult.Missing();
        if (role.IsSystem || await roles.CountUsersAsync(role.Id, ct) > 0) return AdminChangeResult.Denied();

        var roleUserIds = await users.FindIdsByRoleIdAsync(role.Id, ct);
        if (roleUserIds.Count > 0 && role.Permissions.Any(SensitivePermissions.IsSensitive))
        {
            var now = clock.UtcNow;
            foreach (var userId in roleUserIds)
            {
                var user = await users.FindByIdWithPermissionsAsync(userId, ct: ct);
                if (user is null) continue;
                var remainingRoles = (await roles.FindByIdsAsync(user.Roles.Select(x => x.RoleId), ct))
                    .Where(x => x.Id != role.Id).ToList();
                var effectiveBefore = EffectivePermissions.Resolve(remainingRoles.Append(role), user.PermissionOverrides);
                var effectiveAfter = EffectivePermissions.Resolve(remainingRoles, user.PermissionOverrides);
                if (RbacCommandSupport.HasLostSensitivePermission(effectiveBefore, effectiveAfter))
                {
                    await RbacCommandSupport.RevokeUserSessionsAsync(userId, sessions, sessionCache, now, ct);
                }
            }
        }

        roles.Delete(role);
        invalidation.Invalidate();
        await audit.WriteAsync("role_deleted", request.GrantorUserId, details: new { roleId = role.Id }, ct: ct);
        return new(true);
    }
}

public sealed class SetUserPermissionOverrideCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ISessionRepository sessions,
    ISessionCacheHook sessionCache,
    ISessionReconciliationService reconciliation,
    IRbacInvalidationToken invalidation,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<SetUserPermissionOverrideCommand, AdminChangeResult>
{
    public async ValueTask<AdminChangeResult> Handle(SetUserPermissionOverrideCommand request, CancellationToken ct)
    {
        if (!PermissionCatalog.IsKnown(request.Permission)) return AdminChangeResult.Denied();
        var grantor = await RbacCommandSupport.EffectiveAsync(request.GrantorUserId, users, roles, ct);
        if (request.Effect == PermissionEffect.Grant && !RbacCommandSupport.CanGrant(grantor, [request.Permission]))
            return AdminChangeResult.Denied();

        var user = await users.FindByIdWithPermissionsAsync(request.UserId, track: true, ct: ct);
        if (user is null) return AdminChangeResult.Missing();
        var assigned = await roles.FindByIdsAsync(user.Roles.Select(x => x.RoleId), ct);
        var permissionsBefore = EffectivePermissions.Resolve(assigned, user.PermissionOverrides);

        var now = clock.UtcNow;
        if (request.Effect is { } effect) user.SetPermissionOverride(request.Permission, effect, request.GrantorUserId, now);
        else user.ClearPermissionOverride(request.Permission);
        users.Update(user);

        var permissionsAfter = EffectivePermissions.Resolve(assigned, user.PermissionOverrides);

        if (RbacCommandSupport.HasLostSensitivePermission(permissionsBefore, permissionsAfter))
        {
            await RbacCommandSupport.RevokeUserSessionsAsync(user.Id, sessions, sessionCache, now, ct);
        }

        var hadMulti = permissionsBefore.Contains(PermissionCatalog.SessionMulti);
        var hasMulti = permissionsAfter.Contains(PermissionCatalog.SessionMulti);
        if (hadMulti && !hasMulti) await reconciliation.ReconcileAfterMultiSessionLossAsync(user.Id, ct);

        invalidation.Invalidate();
        await audit.WriteAsync("user_permission_override_updated", request.UserId,
            details: new { grantorUserId = request.GrantorUserId, permission = request.Permission, effect = request.Effect?.ToString() }, ct: ct);
        return new(true);
    }
}

public sealed class ElevateUserRolesCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ISessionRepository sessions,
    ISessionCacheHook sessionCache,
    ISessionReconciliationService reconciliation,
    IRbacInvalidationToken invalidation,
    IPlatformClock clock,
    ISecurityAuditWriter audit) : IRequestHandler<ElevateUserRolesCommand, AdminChangeResult>
{
    public async ValueTask<AdminChangeResult> Handle(ElevateUserRolesCommand request, CancellationToken ct)
    {
        var grantor = await RbacCommandSupport.EffectiveAsync(request.GrantorUserId, users, roles, ct);
        var target = await users.FindByIdWithPermissionsAsync(request.UserId, track: true, ct: ct);
        if (target is null) return AdminChangeResult.Missing();
        var requested = await roles.FindByIdsAsync(request.RoleIds.Distinct(), ct);
        if (requested.Count != request.RoleIds.Distinct().Count()) return AdminChangeResult.Missing();
        if (!RbacCommandSupport.CanGrant(grantor, requested.SelectMany(x => x.Permissions))) return AdminChangeResult.Denied();

        var priorRoles = await roles.FindByIdsAsync(target.Roles.Select(x => x.RoleId), ct);
        var permissionsBefore = EffectivePermissions.Resolve(priorRoles, target.PermissionOverrides);

        var now = clock.UtcNow;
        foreach (var role in target.Roles.ToArray()) target.RemoveRole(role.RoleId);
        foreach (var role in requested) target.AssignRole(role.Id, request.GrantorUserId, now);
        users.Update(target);

        var permissionsAfter = EffectivePermissions.Resolve(requested, target.PermissionOverrides);

        if (RbacCommandSupport.HasLostSensitivePermission(permissionsBefore, permissionsAfter))
        {
            await RbacCommandSupport.RevokeUserSessionsAsync(target.Id, sessions, sessionCache, now, ct);
        }

        var hadMulti = permissionsBefore.Contains(PermissionCatalog.SessionMulti);
        var hasMulti = permissionsAfter.Contains(PermissionCatalog.SessionMulti);
        if (hadMulti && !hasMulti) await reconciliation.ReconcileAfterMultiSessionLossAsync(target.Id, ct);

        invalidation.Invalidate();
        await audit.WriteAsync("user_roles_updated", target.Id,
            details: new { grantorUserId = request.GrantorUserId, roleIds = requested.Select(x => x.Id).ToArray() }, ct: ct);
        return new(true);
    }
}
