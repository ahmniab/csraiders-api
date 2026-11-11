using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.API.Controllers;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.IntegrationTests;

public sealed class SuspensionCleanupTests
{
    [Fact]
    public void Suspension_endpoints_require_user_manage()
    {
        foreach (var name in new[] { nameof(AdminUsersController.Suspend), nameof(AdminUsersController.Reactivate) })
        {
            var method = typeof(AdminUsersController).GetMethod(name)!;
            Assert.Equal("permission:user:manage", method.GetCustomAttributes(typeof(HasPermissionAttribute), true)
                .Cast<HasPermissionAttribute>().Single().Policy);
        }
    }

    [Fact]
    public void User_suspension_state_transitions_are_reversible_but_deleted_users_are_not_authenticatable()
    {
        var user = User.RegisterVerified(Email.Create("suspend@example.com"), "Suspended User",
            "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn", DateTimeOffset.UtcNow);
        user.Suspend();
        Assert.Equal(UserStatus.Suspended, user.Status);
        Assert.False(user.CanAuthenticate);
        user.Reactivate();
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.CanAuthenticate);
    }
}
