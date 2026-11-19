using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using FluentAssertions;

namespace CsRaiders.Api.Domain.UnitTests;

public sealed class PermissionModelTests
{
    [Fact]
    public void Catalog_contains_the_current_permission_set_without_duplicates()
    {
        PermissionCatalog.All.Should().BeEquivalentTo(
        [
            PermissionCatalog.CourseRead,
            PermissionCatalog.CourseWrite,
            PermissionCatalog.UserRead,
            PermissionCatalog.UserWrite,
            PermissionCatalog.RoleManage,
            PermissionCatalog.PaymentRead,
            PermissionCatalog.PaymentManage,
            PermissionCatalog.NotificationSend,
            PermissionCatalog.AnalyticsRead,
            PermissionCatalog.AuditRead,
            PermissionCatalog.SettingsRead,
            PermissionCatalog.SettingsWrite,
            PermissionCatalog.SessionMulti,
            PermissionCatalog.SessionRevoke
        ]);
        PermissionCatalog.All.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(PermissionCatalog.CourseRead, true)]
    [InlineData(PermissionCatalog.PaymentManage, true)]
    [InlineData("unknown:permission", false)]
    public void IsKnown_reflects_the_catalog(string permission, bool expected)
        => PermissionCatalog.IsKnown(permission).Should().Be(expected);

    [Fact]
    public void Student_allowed_permissions_are_limited_to_read_and_multi_session()
    {
        PermissionCatalog.StudentAllowed.Should().BeEquivalentTo(
            [PermissionCatalog.CourseRead, PermissionCatalog.SessionMulti]);
    }

    [Fact]
    public void Sensitive_permissions_match_the_current_catalog()
    {
        SensitivePermissions.All.Should().BeEquivalentTo(
        [
            PermissionCatalog.RoleManage,
            PermissionCatalog.PaymentManage,
            PermissionCatalog.UserWrite,
            PermissionCatalog.SessionRevoke
        ]);
    }

    [Fact]
    public void Effective_permissions_union_roles_and_grants_while_deny_wins()
    {
        var role = RoleFactory.Create("Staff", "staff", RoleScope.Staff,
            [PermissionCatalog.CourseRead, PermissionCatalog.CourseWrite]);
        var user = UserFactory.RegisterVerified(
            CsRaiders.Api.Domain.ValueObjects.Email.Create("user@example.com"),
            "User",
            "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn",
            DateTimeOffset.UtcNow);

        user.SetPermissionOverride(PermissionCatalog.CourseWrite, PermissionEffect.Deny, Guid.NewGuid(), DateTimeOffset.UtcNow);
        user.SetPermissionOverride(PermissionCatalog.PaymentManage, PermissionEffect.Grant, Guid.NewGuid(), DateTimeOffset.UtcNow);

        var effective = EffectivePermissions.Resolve([role], user.PermissionOverrides);

        effective.Should().Contain(PermissionCatalog.CourseRead);
        effective.Should().Contain(PermissionCatalog.PaymentManage);
        effective.Should().NotContain(PermissionCatalog.CourseWrite);
    }
}
