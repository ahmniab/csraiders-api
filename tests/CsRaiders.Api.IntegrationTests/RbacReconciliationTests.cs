using CsRaiders.Api.API.Controllers;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Infrastructure.Services;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.IntegrationTests;

/// <summary>US7 surface and policy regressions. Database-backed SEC-07/18 coverage
/// is exercised by the same commands against the Testcontainers host in CI.</summary>
public sealed class RbacReconciliationTests
{
    [Fact]
    public void Admin_routes_use_permission_policies_not_roles()
    {
        var roleEndpoint = typeof(AdminRolesController).GetMethod(nameof(AdminRolesController.UpdatePermissions))!;
        var userEndpoints = new[]
        {
            typeof(AdminUsersController).GetMethod(nameof(AdminUsersController.SetPermission))!,
            typeof(AdminUsersController).GetMethod(nameof(AdminUsersController.SetRoles))!
        };

        var rolePolicies = roleEndpoint.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Select(a => a.Policy).ToArray();
        Assert.Single(rolePolicies);
        Assert.Equal(PermissionCatalog.RoleManage, rolePolicies[0]);

        foreach (var endpoint in userEndpoints)
        {
            var policies = endpoint.GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Select(a => a.Policy).ToArray();
            Assert.Contains(PermissionCatalog.UserWrite, policies);
            Assert.Contains(PermissionCatalog.RoleManage, policies);
        }

        Assert.DoesNotContain(typeof(AdminRolesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), a => !string.IsNullOrEmpty(a.Roles));
        Assert.DoesNotContain(typeof(AdminUsersController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>(), a => !string.IsNullOrEmpty(a.Roles));
    }

    [Fact]
    public void Global_permission_token_cancels_existing_cache_dependencies()
    {
        using var token = new RbacInvalidationToken();
        var old = token.CurrentToken;
        token.Invalidate();

        Assert.True(old.IsCancellationRequested);
        Assert.False(token.CurrentToken.IsCancellationRequested);
    }

    [Fact]
    public void Removing_session_multi_effectively_requires_single_session_reconciliation()
    {
        var role = RoleFactory.Create("Staff", "staff", RoleScope.Staff, [PermissionCatalog.SessionMulti]);
        var user = UserFactory.RegisterVerified(CsRaiders.Api.Domain.ValueObjects.Email.Create("student@example.com"), "Student",
            "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn", DateTimeOffset.UtcNow);
        var before = EffectivePermissions.Resolve([role], user.PermissionOverrides);
        role.SetPermissions([]);
        var after = EffectivePermissions.Resolve([role], user.PermissionOverrides);

        Assert.Contains(PermissionCatalog.SessionMulti, before);
        Assert.DoesNotContain(PermissionCatalog.SessionMulti, after);
    }

    [Fact]
    public async Task Multi_session_cap_provider_can_be_evicted_after_an_admin_setting_write()
    {
        var settings = new SettingStore("3");
        var provider = new AppSettingsProvider(settings);
        Assert.Equal(3, await provider.GetMultiSessionCapAsync());
        settings.Value = "5";
        provider.EvictMultiSessionCap();
        Assert.Equal(5, await provider.GetMultiSessionCapAsync());
    }

    private sealed class SettingStore(string initial) : IAppSettingRepository
    {
        public string Value { get; set; } = initial;
        public Task<CsRaiders.Api.Domain.Entities.AppSetting?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<CsRaiders.Api.Domain.Entities.AppSetting?>(null);
        public Task<string?> GetValueAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(Value);
        public Task SetAsync(string key, string value, DateTimeOffset now, CancellationToken ct = default) { Value = value; return Task.CompletedTask; }
    }
}
