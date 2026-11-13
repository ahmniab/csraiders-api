namespace CsRaiders.Api.Domain.Constants;

public static class PermissionBitmaskMapper
{
    private static readonly Dictionary<string, ulong> StringToBit = new(StringComparer.Ordinal)
    {
        [PermissionCatalog.CourseRead] = (ulong)PermissionBits.CourseRead,
        [PermissionCatalog.CourseWrite] = (ulong)PermissionBits.CourseWrite,
        [PermissionCatalog.CoursePurge] = (ulong)PermissionBits.CoursePurge,
        [PermissionCatalog.LessonWrite] = (ulong)PermissionBits.LessonWrite,
        [PermissionCatalog.AssetWrite] = (ulong)PermissionBits.AssetWrite,
        [PermissionCatalog.QuestionWrite] = (ulong)PermissionBits.QuestionWrite,
        [PermissionCatalog.QuizWrite] = (ulong)PermissionBits.QuizWrite,
        [PermissionCatalog.UserRead] = (ulong)PermissionBits.UserRead,
        [PermissionCatalog.UserWrite] = (ulong)PermissionBits.UserWrite,
        [PermissionCatalog.RoleManage] = (ulong)PermissionBits.RoleManage,
        [PermissionCatalog.PaymentRead] = (ulong)PermissionBits.PaymentRead,
        [PermissionCatalog.PaymentVerify] = (ulong)PermissionBits.PaymentVerify,
        [PermissionCatalog.PaymentRefund] = (ulong)PermissionBits.PaymentRefund,
        [PermissionCatalog.NotificationSend] = (ulong)PermissionBits.NotificationSend,
        [PermissionCatalog.AnalyticsRead] = (ulong)PermissionBits.AnalyticsRead,
        [PermissionCatalog.AuditRead] = (ulong)PermissionBits.AuditRead,
        [PermissionCatalog.SettingsRead] = (ulong)PermissionBits.SettingsRead,
        [PermissionCatalog.SettingsWrite] = (ulong)PermissionBits.SettingsWrite,
        [PermissionCatalog.SessionMulti] = (ulong)PermissionBits.SessionMulti,
        [PermissionCatalog.SessionRevoke] = (ulong)PermissionBits.SessionRevoke
    };

    private static readonly (ulong Bit, string Name)[] BitToName =
    [
        ((ulong)PermissionBits.CourseRead, PermissionCatalog.CourseRead),
        ((ulong)PermissionBits.CourseWrite, PermissionCatalog.CourseWrite),
        ((ulong)PermissionBits.CoursePurge, PermissionCatalog.CoursePurge),
        ((ulong)PermissionBits.LessonWrite, PermissionCatalog.LessonWrite),
        ((ulong)PermissionBits.AssetWrite, PermissionCatalog.AssetWrite),
        ((ulong)PermissionBits.QuestionWrite, PermissionCatalog.QuestionWrite),
        ((ulong)PermissionBits.QuizWrite, PermissionCatalog.QuizWrite),
        ((ulong)PermissionBits.UserRead, PermissionCatalog.UserRead),
        ((ulong)PermissionBits.UserWrite, PermissionCatalog.UserWrite),
        ((ulong)PermissionBits.RoleManage, PermissionCatalog.RoleManage),
        ((ulong)PermissionBits.PaymentRead, PermissionCatalog.PaymentRead),
        ((ulong)PermissionBits.PaymentVerify, PermissionCatalog.PaymentVerify),
        ((ulong)PermissionBits.PaymentRefund, PermissionCatalog.PaymentRefund),
        ((ulong)PermissionBits.NotificationSend, PermissionCatalog.NotificationSend),
        ((ulong)PermissionBits.AnalyticsRead, PermissionCatalog.AnalyticsRead),
        ((ulong)PermissionBits.AuditRead, PermissionCatalog.AuditRead),
        ((ulong)PermissionBits.SettingsRead, PermissionCatalog.SettingsRead),
        ((ulong)PermissionBits.SettingsWrite, PermissionCatalog.SettingsWrite),
        ((ulong)PermissionBits.SessionMulti, PermissionCatalog.SessionMulti),
        ((ulong)PermissionBits.SessionRevoke, PermissionCatalog.SessionRevoke)
    ];

    public static ulong GetBit(string? permissionName)
    {
        if (string.IsNullOrEmpty(permissionName))
            return 0UL;

        return StringToBit.TryGetValue(permissionName, out var bit) ? bit : 0UL;
    }

    public static ulong ToBitmask(IEnumerable<string>? permissionNames)
    {
        if (permissionNames is null)
            return 0UL;

        ulong mask = 0UL;
        foreach (var name in permissionNames)
        {
            if (!string.IsNullOrEmpty(name) && StringToBit.TryGetValue(name, out var bit))
            {
                mask |= bit;
            }
        }
        return mask;
    }

    public static bool HasPermission(ulong bitmask, ulong requiredBit)
    {
        return requiredBit != 0UL && (bitmask & requiredBit) != 0UL;
    }

    public static IReadOnlyList<string> ToPermissionNames(ulong bitmask)
    {
        if (bitmask == 0UL)
            return [];

        var list = new List<string>();
        foreach (var (bit, name) in BitToName)
        {
            if ((bitmask & bit) != 0UL)
            {
                list.Add(name);
            }
        }
        return list;
    }
}
