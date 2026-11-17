namespace CsRaiders.Api.Domain.Constants;

public static class SensitivePermissions
{
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PermissionCatalog.RoleManage,
        PermissionCatalog.PaymentRefund,
        PermissionCatalog.UserWrite,
        PermissionCatalog.SessionRevoke
    };

    public static bool IsSensitive(string? permission) =>
        !string.IsNullOrEmpty(permission) && All.Contains(permission);

    public static bool ContainsAny(IEnumerable<string>? permissions) =>
        permissions is not null && permissions.Any(IsSensitive);
}
