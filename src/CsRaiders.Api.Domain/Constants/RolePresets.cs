using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Domain.Constants;

public sealed record RolePreset(string Name, string Slug, RoleScope Scope, IReadOnlyList<string> Permissions);

public static class RolePresets
{
    private const string Multi = PermissionCatalog.SessionMulti;

    public static readonly RolePreset Student = new("Student", "student", RoleScope.Student,
        [PermissionCatalog.CourseRead]);                       // session:multi is OFF by default (FR-61)

    public static readonly RolePreset Support = new("Support Agent", "support", RoleScope.Staff,
    [
        PermissionCatalog.CourseRead, PermissionCatalog.UserRead, PermissionCatalog.UserWrite,
        PermissionCatalog.PaymentRead, PermissionCatalog.SessionRevoke, Multi
    ]);

    public static readonly RolePreset ContentManager = new("Content Manager", "content-manager", RoleScope.Staff,
    [
        PermissionCatalog.CourseRead, PermissionCatalog.CourseWrite, PermissionCatalog.LessonWrite,
        PermissionCatalog.AssetWrite, PermissionCatalog.QuestionWrite, PermissionCatalog.QuizWrite, Multi
    ]);

    // Decisions D1/D2: only Super Admin holds role:manage and course:purge.
    public static readonly RolePreset Administrator = new("Administrator", "admin", RoleScope.Staff,
        PermissionCatalog.All
            .Where(p => p is not (PermissionCatalog.RoleManage or PermissionCatalog.CoursePurge))
            .ToArray());

    public static readonly RolePreset SuperAdmin = new("Super Admin", "super-admin", RoleScope.Staff,
        PermissionCatalog.All);

    public static readonly IReadOnlyList<RolePreset> All = [Student, Support, ContentManager, Administrator, SuperAdmin];
}
