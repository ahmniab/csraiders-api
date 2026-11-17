using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Entities;

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

public sealed class UserPermission
{
    public Guid UserId { get; set; }
    public string Permission { get; set; } = null!;
    public PermissionEffect Effect { get; set; }
    public Guid GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}
