namespace CsRaiders.Api.Domain.Constants;

public static class PermissionCatalog
{
    public const string CourseRead = "course:read";
    public const string CourseWrite = "course:write";
    public const string CoursePurge = "course:purge";   // [Proposal] hard delete, Super Admin only
    public const string LessonWrite = "lesson:write";
    public const string AssetWrite = "asset:write";
    public const string QuestionWrite = "question:write";
    public const string QuizWrite = "quiz:write";
    public const string UserRead = "user:read";
    public const string UserWrite = "user:write";
    public const string RoleManage = "role:manage";
    public const string PaymentRead = "payment:read";
    public const string PaymentVerify = "payment:verify";
    public const string PaymentRefund = "payment:refund";
    public const string NotificationSend = "notification:send";
    public const string AnalyticsRead = "analytics:read";
    public const string AuditRead = "audit:read";
    public const string SettingsRead = "settings:read";
    public const string SettingsWrite = "settings:write";
    public const string SessionMulti = "session:multi";
    public const string SessionRevoke = "session:revoke";

    public static IReadOnlyList<string> All { get; } =
    [
        CourseRead, CourseWrite, CoursePurge, LessonWrite, AssetWrite, QuestionWrite, QuizWrite,
        UserRead, UserWrite, RoleManage, PaymentRead, PaymentVerify, PaymentRefund,
        NotificationSend, AnalyticsRead, AuditRead, SettingsRead, SettingsWrite,
        SessionMulti, SessionRevoke
    ];

    /// <summary>[Proposal] The only permissions a Student-scope role may ever hold (M0 T4.3).</summary>
    public static IReadOnlyList<string> StudentAllowed { get; } = [CourseRead, SessionMulti];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);
    public static bool IsKnown(string permission) => Known.Contains(permission);
}
