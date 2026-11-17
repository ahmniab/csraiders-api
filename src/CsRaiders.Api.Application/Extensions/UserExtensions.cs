using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Extensions;

public static class UserExtensions
{
    public static bool CanAuthenticate(this User user)
        => user.Status == UserStatus.Active && user.DeletedAt is null;

    /// <summary>Real password change (reset flow). Updates password_changed_at.</summary>
    public static void ChangePassword(this User user, string newHash, DateTimeOffset now)
    {
        user.PasswordHash = PasswordHashGuard.EnsureHashed(newHash);
        user.PasswordChangedAt = now;
    }

    /// <summary>Transparent Argon2id parameter upgrade on login. Same password, so password_changed_at is untouched.</summary>
    public static void UpgradePasswordHash(this User user, string newHash)
        => user.PasswordHash = PasswordHashGuard.EnsureHashed(newHash);

    public static void Suspend(this User user)
    {
        if (user.Status == UserStatus.Deleted) throw new DomainException("user_deleted", "User is deleted.");
        user.Status = UserStatus.Suspended;
    }

    public static void Reactivate(this User user)
    {
        if (user.Status != UserStatus.Suspended) throw new DomainException("user_not_suspended", "User is not suspended.");
        user.Status = UserStatus.Active;
    }

    /// <summary>Soft delete only; PII anonymization is deferred (FR-70).</summary>
    public static void SoftDelete(this User user, DateTimeOffset now)
    {
        user.Status = UserStatus.Deleted;
        user.DeletedAt = now;
    }

    public static void AssignRole(this User user, Guid roleId, Guid? grantedBy, DateTimeOffset now)
    {
        if (user.Roles.Any(r => r.RoleId == roleId)) return;
        user.Roles.Add(new UserRole { UserId = user.Id, RoleId = roleId, GrantedBy = grantedBy, GrantedAt = now });
    }

    public static bool RemoveRole(this User user, Guid roleId)
    {
        var existing = user.Roles.Find(r => r.RoleId == roleId);
        return existing is not null && user.Roles.Remove(existing);
    }

    /// <summary>Per-user grant or DENY (FR-62.2, FR-74, decision D4). Updates in place so EF never sees delete+insert of one key.</summary>
    public static void SetPermissionOverride(this User user, string permission, PermissionEffect effect, Guid grantedBy, DateTimeOffset now)
    {
        if (!PermissionCatalog.IsKnown(permission))
            throw new DomainException("unknown_permission", $"Unknown permission '{permission}'.");

        var existing = user.PermissionOverrides.Find(o => o.Permission == permission);
        if (existing is null)
            user.PermissionOverrides.Add(new UserPermission
                { UserId = user.Id, Permission = permission, Effect = effect, GrantedBy = grantedBy, GrantedAt = now });
        else
            (existing.Effect, existing.GrantedBy, existing.GrantedAt) = (effect, grantedBy, now);
    }

    public static bool ClearPermissionOverride(this User user, string permission) =>
        user.PermissionOverrides.RemoveAll(o => o.Permission == permission) > 0;
}
