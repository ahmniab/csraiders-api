using System.Security.Claims;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CsRaiders.Api.IntegrationTests;

/// <summary>
/// Policy regressions for the claim-based authorization model: every catalog
/// permission gets a policy that requires a "permission" claim with that value.
/// </summary>
public sealed class PermissionAuthorizationHandlerTests
{
    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AuthPolicies.AddAuthPolicies);

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    [Fact]
    public void Auth_policies_register_the_authenticated_only_policy_and_every_catalog_permission()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(AuthPolicies.AddAuthPolicies);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthorizationOptions>>().Value;

        Assert.True(options.GetPolicy(AuthPolicies.AuthenticatedOnly) is not null);
        foreach (var permission in PermissionCatalog.All)
        {
            Assert.True(options.GetPolicy(permission) is not null,
                $"Expected a policy named '{permission}'.");
        }
    }

    [Theory]
    [InlineData(PermissionCatalog.CourseRead)]
    [InlineData(PermissionCatalog.CourseWrite)]
    [InlineData(PermissionCatalog.UserWrite)]
    [InlineData(PermissionCatalog.RoleManage)]
    [InlineData(PermissionCatalog.PaymentManage)]
    [InlineData(PermissionCatalog.SessionRevoke)]
    public async Task Allows_request_when_permission_claim_matches_the_policy(string permission)
    {
        var authService = BuildAuthorizationService();
        var principal = CreatePrincipalWithPermissions([permission]);

        var result = await authService.AuthorizeAsync(principal, null, permission);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Allows_request_when_multiple_permission_claims_are_present()
    {
        var authService = BuildAuthorizationService();
        var principal = CreatePrincipalWithPermissions(
            [PermissionCatalog.CourseRead, PermissionCatalog.RoleManage]);

        var readResult = await authService.AuthorizeAsync(principal, null, PermissionCatalog.CourseRead);
        var roleResult = await authService.AuthorizeAsync(principal, null, PermissionCatalog.RoleManage);

        Assert.True(readResult.Succeeded);
        Assert.True(roleResult.Succeeded);
    }

    [Theory]
    [InlineData(PermissionCatalog.CourseWrite)]
    [InlineData(PermissionCatalog.UserWrite)]
    [InlineData(PermissionCatalog.SessionRevoke)]
    public async Task Denies_request_when_permission_claim_is_absent(string missingPermission)
    {
        var authService = BuildAuthorizationService();
        var principal = CreatePrincipalWithPermissions([PermissionCatalog.CourseRead]);

        var result = await authService.AuthorizeAsync(principal, null, missingPermission);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Denies_request_when_permission_claim_is_missing_entirely()
    {
        var authService = BuildAuthorizationService();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("sid", Guid.NewGuid().ToString())],
            "TestAuth"));

        var result = await authService.AuthorizeAsync(principal, null, PermissionCatalog.CourseRead);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Authenticated_only_policy_requires_the_session_claim()
    {
        var authService = BuildAuthorizationService();

        var withSession = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("sid", Guid.NewGuid().ToString())],
            "TestAuth"));
        var withoutSession = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString())],
            "TestAuth"));

        Assert.True((await authService.AuthorizeAsync(withSession, null, AuthPolicies.AuthenticatedOnly)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(withoutSession, null, AuthPolicies.AuthenticatedOnly)).Succeeded);
    }

    [Fact]
    public async Task Unknown_policy_names_are_not_registered()
    {
        var authService = BuildAuthorizationService();
        var principal = CreatePrincipalWithPermissions(PermissionCatalog.All);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => authService.AuthorizeAsync(principal, null, "unknown:unregistered:perm"));
    }

    private static ClaimsPrincipal CreatePrincipalWithPermissions(IEnumerable<string> permissions)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("sid", Guid.NewGuid().ToString())
        };
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
