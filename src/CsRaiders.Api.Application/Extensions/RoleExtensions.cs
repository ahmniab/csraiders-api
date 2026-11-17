using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;

namespace CsRaiders.Api.Application.Extensions;

public static class RoleExtensions
{
    /// <summary>Replaces the permission set and returns the diff so the caller knows whether to reconcile sessions.</summary>
    public static PermissionChange SetPermissions(this Role role, IEnumerable<string> permissions)
    {
        var next = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        var unknown = next.Where(p => !PermissionCatalog.IsKnown(p)).ToList();
        if (unknown.Count > 0)
            throw new DomainException("unknown_permission", $"Unknown permission(s): {string.Join(", ", unknown)}.");

        if (role.Scope == RoleScope.Student && next.Any(p => !PermissionCatalog.StudentAllowed.Contains(p)))
            throw new DomainException("student_role_forbidden_permission", "Student roles cannot hold staff permissions.");

        var change = new PermissionChange(next.Except(role.Permissions).ToArray(), role.Permissions.Except(next).ToArray());
        role.Permissions = next;
        return change;
    }

    public static void UpdateDetails(this Role role, string name, string slug, RoleScope scope)
    {
        if (role.IsSystem) throw new DomainException("system_role_immutable", "System roles cannot be edited.");
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        role.Name = name.Trim();
        role.Slug = slug.Trim().ToLowerInvariant();
        role.Scope = scope;
        if (role.Scope == RoleScope.Student && role.Permissions.Any(p => !PermissionCatalog.StudentAllowed.Contains(p)))
            throw new DomainException("student_role_forbidden_permission", "Student roles cannot hold staff permissions.");
    }
}
