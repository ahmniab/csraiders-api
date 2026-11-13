using System.Security.Claims;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.Domain.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CsRaiders.Api.IntegrationTests;

public sealed class PermissionAuthorizationHandlerTests
{
    private readonly PermissionAuthorizationHandler _handler = new();

    [Fact]
    public void Handler_has_parameterless_constructor_and_no_database_or_cache_dependencies()
    {
        var constructors = typeof(PermissionAuthorizationHandler).GetConstructors();
        Assert.Single(constructors);
        Assert.Empty(constructors[0].GetParameters());
    }

    [Theory]
    [InlineData(PermissionCatalog.CourseRead, 1UL << 0)]
    [InlineData(PermissionCatalog.CourseWrite, 1UL << 1)]
    [InlineData(PermissionCatalog.UserWrite, 1UL << 8)]
    [InlineData(PermissionCatalog.RoleManage, 1UL << 9)]
    [InlineData(PermissionCatalog.SessionRevoke, 1UL << 19)]
    public async Task Allows_request_when_bitmask_contains_required_permission(string permission, ulong requiredBit)
    {
        var principal = CreatePrincipalWithPerms(requiredBit.ToString());
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        Assert.False(context.HasFailed);
    }

    [Fact]
    public async Task Allows_request_when_multiple_permissions_are_represented_in_bitmask()
    {
        // Combined CourseRead (bit 0) and RoleManage (bit 9)
        ulong combinedMask = (1UL << 0) | (1UL << 9);
        var principal = CreatePrincipalWithPerms(combinedMask.ToString());

        var reqCourseRead = new PermissionRequirement(PermissionCatalog.CourseRead);
        var context1 = new AuthorizationHandlerContext([reqCourseRead], principal, null);
        await _handler.HandleAsync(context1);
        Assert.True(context1.HasSucceeded);

        var reqRoleManage = new PermissionRequirement(PermissionCatalog.RoleManage);
        var context2 = new AuthorizationHandlerContext([reqRoleManage], principal, null);
        await _handler.HandleAsync(context2);
        Assert.True(context2.HasSucceeded);
    }

    [Theory]
    [InlineData(PermissionCatalog.CourseWrite)]
    [InlineData(PermissionCatalog.UserWrite)]
    [InlineData(PermissionCatalog.SessionRevoke)]
    public async Task Denies_request_when_bitmask_lacks_required_permission(string missingPermission)
    {
        // Bitmask only has CourseRead (bit 0)
        ulong bitmask = 1UL << 0;
        var principal = CreatePrincipalWithPerms(bitmask.ToString());
        var requirement = new PermissionRequirement(missingPermission);
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Denies_request_when_perms_claim_is_missing()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", Guid.NewGuid().ToString())], "TestAuth"));
        var requirement = new PermissionRequirement(PermissionCatalog.CourseRead);
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("+100")]
    [InlineData("1.5")]
    [InlineData("0x10")]
    [InlineData("18446744073709551616")] // ulong.MaxValue + 1
    public async Task Denies_request_when_perms_claim_is_malformed(string malformedPerms)
    {
        var principal = CreatePrincipalWithPerms(malformedPerms);
        var requirement = new PermissionRequirement(PermissionCatalog.CourseRead);
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Denies_request_when_requirement_is_not_a_recognized_permission()
    {
        const ulong fullMask = (1UL << 20) - 1;
        var principal = CreatePrincipalWithPerms(fullMask.ToString());
        var requirement = new PermissionRequirement("unknown:unregistered:perm");
        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task End_to_end_authorization_pipeline_evaluates_bitmask_policies_correctly()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionAuthorizationPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        using var provider = services.BuildServiceProvider();
        var authService = provider.GetRequiredService<IAuthorizationService>();

        ulong authorizedMask = (1UL << 0) | (1UL << 8); // CourseRead (bit 0), UserWrite (bit 8)
        var authorizedUser = CreatePrincipalWithPerms(authorizedMask.ToString());

        var readResult = await authService.AuthorizeAsync(authorizedUser, null, "permission:course:read");
        Assert.True(readResult.Succeeded);

        var writeResult = await authService.AuthorizeAsync(authorizedUser, null, "permission:user:write");
        Assert.True(writeResult.Succeeded);

        var deniedResult = await authService.AuthorizeAsync(authorizedUser, null, "permission:course:purge");
        Assert.False(deniedResult.Succeeded);

        var missingPermsUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "TestAuth"));
        var missingResult = await authService.AuthorizeAsync(missingPermsUser, null, "permission:course:read");
        Assert.False(missingResult.Succeeded);
    }

    private static ClaimsPrincipal CreatePrincipalWithPerms(string permsValue)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("sid", Guid.NewGuid().ToString()),
            new Claim("perms", permsValue)
        ], "TestAuth");

        return new ClaimsPrincipal(identity);
    }
}
