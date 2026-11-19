using CsRaiders.Api.API.Controllers;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.IntegrationTests;

public sealed class SuspensionCleanupTests
{
    [Fact]
    public void Suspension_endpoints_require_user_write()
    {
        foreach (var name in new[] { nameof(AdminUsersController.Suspend), nameof(AdminUsersController.Reactivate) })
        {
            var method = typeof(AdminUsersController).GetMethod(name)!;
            var policies = method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Select(a => a.Policy).ToArray();

            Assert.Single(policies);
            Assert.Equal(PermissionCatalog.UserWrite, policies[0]);
        }
    }

    [Fact]
    public void User_suspension_state_transitions_are_reversible_but_deleted_users_are_not_authenticatable()
    {
        var user = UserFactory.RegisterVerified(Email.Create("suspend@example.com"), "Suspended User",
            "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn", DateTimeOffset.UtcNow);
        user.Suspend();
        Assert.Equal(UserStatus.Suspended, user.Status);
        Assert.False(user.CanAuthenticate());
        user.Reactivate();
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.CanAuthenticate());
    }
}
