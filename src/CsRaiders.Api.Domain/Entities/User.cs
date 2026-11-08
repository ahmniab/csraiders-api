using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

public sealed class User : Entity
{
    private readonly List<UserRole> _roles = [];
    private readonly List<UserPermission> _permissionOverrides = [];

    private User() { }   // EF

    public Email Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Phone { get; private set; }
    public string? WhatsApp { get; private set; }
    public string? AcademicYear { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public short PepperVersion { get; private set; } = 1;
    public UserStatus Status { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;
    public IReadOnlyCollection<UserPermission> PermissionOverrides => _permissionOverrides;

    public bool CanAuthenticate => Status == UserStatus.Active && DeletedAt is null;

    /// <summary>Created at the end of OTP verification (decision D3): the email is verified by definition.</summary>
    public static User RegisterVerified(Email email, string name, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 200) throw new DomainException("name_too_long", "Name is too long.");

        return new User
        {
            Email = email,
            Name = name.Trim(),
            PasswordHash = PasswordHashGuard.EnsureHashed(passwordHash),
            Status = UserStatus.Active,
            EmailVerifiedAt = now,
            PasswordChangedAt = now,
            CreatedAt = now
        };
    }

    /// <summary>Real password change (reset flow). Updates password_changed_at.</summary>
    public void ChangePassword(string newHash, DateTimeOffset now)
    {
        PasswordHash = PasswordHashGuard.EnsureHashed(newHash);
        PasswordChangedAt = now;
    }

    /// <summary>Transparent Argon2id parameter upgrade on login. Same password, so password_changed_at is untouched.</summary>
    public void UpgradePasswordHash(string newHash) => PasswordHash = PasswordHashGuard.EnsureHashed(newHash);

    public void Suspend()
    {
        if (Status == UserStatus.Deleted) throw new DomainException("user_deleted", "User is deleted.");
        Status = UserStatus.Suspended;
    }

    public void Reactivate()
    {
        if (Status != UserStatus.Suspended) throw new DomainException("user_not_suspended", "User is not suspended.");
        Status = UserStatus.Active;
    }

    /// <summary>Soft delete only; PII anonymization is deferred (FR-70).</summary>
    public void SoftDelete(DateTimeOffset now)
    {
        Status = UserStatus.Deleted;
        DeletedAt = now;
    }

    public void AssignRole(Guid roleId, Guid? grantedBy, DateTimeOffset now)
    {
        if (_roles.Any(r => r.RoleId == roleId)) return;
        _roles.Add(new UserRole(Id, roleId, grantedBy, now));
    }

    public bool RemoveRole(Guid roleId)
    {
        var existing = _roles.Find(r => r.RoleId == roleId);
        return existing is not null && _roles.Remove(existing);
    }

    /// <summary>Per-user grant or DENY (FR-62.2, FR-74, decision D4). Updates in place so EF never sees delete+insert of one key.</summary>
    public void SetPermissionOverride(string permission, PermissionEffect effect, Guid grantedBy, DateTimeOffset now)
    {
        if (!PermissionCatalog.IsKnown(permission))
            throw new DomainException("unknown_permission", $"Unknown permission '{permission}'.");

        var existing = _permissionOverrides.Find(o => o.Permission == permission);
        if (existing is null) _permissionOverrides.Add(new UserPermission(Id, permission, effect, grantedBy, now));
        else existing.Update(effect, grantedBy, now);
    }

    public bool ClearPermissionOverride(string permission) =>
        _permissionOverrides.RemoveAll(o => o.Permission == permission) > 0;
}
