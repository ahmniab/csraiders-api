using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Policies;

public static class EffectivePermissions
{
    /// <summary>(union of role permissions + user grants) minus user denies. DENY always wins.</summary>
    public static IReadOnlySet<string> Resolve(IEnumerable<Role> roles, IEnumerable<UserPermission> overrides)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles) set.UnionWith(role.Permissions);

        var list = overrides.ToList();
        set.UnionWith(list.Where(o => o.Effect == PermissionEffect.Grant).Select(o => o.Permission));
        set.ExceptWith(list.Where(o => o.Effect == PermissionEffect.Deny).Select(o => o.Permission));
        return set;
    }
}
