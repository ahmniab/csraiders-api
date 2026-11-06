using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Entities;

public sealed class UserRole
{
    private UserRole() { }
    internal UserRole(Guid userId, Guid roleId, Guid? grantedBy, DateTimeOffset grantedAt)
        => (UserId, RoleId, GrantedBy, GrantedAt) = (userId, roleId, grantedBy, grantedAt);

    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? GrantedBy { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }
}

public sealed class UserPermission
{
    private UserPermission() { }
    internal UserPermission(Guid userId, string permission, PermissionEffect effect, Guid grantedBy, DateTimeOffset grantedAt)
        => (UserId, Permission, Effect, GrantedBy, GrantedAt) = (userId, permission, effect, grantedBy, grantedAt);

    public Guid UserId { get; private set; }
    public string Permission { get; private set; } = null!;
    public PermissionEffect Effect { get; private set; }
    public Guid GrantedBy { get; private set; }
    public DateTimeOffset GrantedAt { get; private set; }

    internal void Update(PermissionEffect effect, Guid grantedBy, DateTimeOffset now)
        => (Effect, GrantedBy, GrantedAt) = (effect, grantedBy, now);
}
