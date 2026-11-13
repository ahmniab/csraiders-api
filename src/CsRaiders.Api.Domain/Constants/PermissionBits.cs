namespace CsRaiders.Api.Domain.Constants;

[Flags]
public enum PermissionBits : ulong
{
    None = 0UL,
    CourseRead = 1UL << 0,
    CourseWrite = 1UL << 1,
    CoursePurge = 1UL << 2,
    LessonWrite = 1UL << 3,
    AssetWrite = 1UL << 4,
    QuestionWrite = 1UL << 5,
    QuizWrite = 1UL << 6,
    UserRead = 1UL << 7,
    UserWrite = 1UL << 8,
    RoleManage = 1UL << 9,
    PaymentRead = 1UL << 10,
    PaymentVerify = 1UL << 11,
    PaymentRefund = 1UL << 12,
    NotificationSend = 1UL << 13,
    AnalyticsRead = 1UL << 14,
    AuditRead = 1UL << 15,
    SettingsRead = 1UL << 16,
    SettingsWrite = 1UL << 17,
    SessionMulti = 1UL << 18,
    SessionRevoke = 1UL << 19
}
