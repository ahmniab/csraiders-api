using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;

namespace CsRaiders.Api.Domain.Entities;

public sealed record PermissionChange(IReadOnlyCollection<string> Added, IReadOnlyCollection<string> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>When true the handler must run session reconciliation (FR-62.1) in the same transaction.</summary>
    public bool TouchesSessionMulti =>
        Added.Contains(PermissionCatalog.SessionMulti) || Removed.Contains(PermissionCatalog.SessionMulti);
}

public sealed class Role : Entity
{
    private List<string> _permissions = [];

    private Role() { }

    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public bool IsSystem { get; private set; }
    public RoleScope Scope { get; private set; }
    public IReadOnlyList<string> Permissions => _permissions;

    public static Role Create(string name, string slug, RoleScope scope, IEnumerable<string> permissions, bool isSystem = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        var role = new Role { Name = name.Trim(), Slug = slug.Trim().ToLowerInvariant(), Scope = scope, IsSystem = isSystem };
        role.SetPermissions(permissions);
        return role;
    }

    /// <summary>Replaces the permission set and returns the diff so the caller knows whether to reconcile sessions.</summary>
    public PermissionChange SetPermissions(IEnumerable<string> permissions)
    {
        var next = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var unknown = next.Where(p => !PermissionCatalog.IsKnown(p)).ToList();
        if (unknown.Count > 0)
            throw new DomainException("unknown_permission", $"Unknown permission(s): {string.Join(", ", unknown)}.");

        if (Scope == RoleScope.Student && next.Any(p => !PermissionCatalog.StudentAllowed.Contains(p)))
            throw new DomainException("student_role_forbidden_permission", "Student roles cannot hold staff permissions.");

        var change = new PermissionChange(next.Except(_permissions).ToArray(), _permissions.Except(next).ToArray());
        _permissions = next;
        return change;
    }
}
