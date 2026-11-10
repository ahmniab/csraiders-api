using System.Text.Json;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.API.Controllers;
using CsRaiders.Api.Application.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.IntegrationTests;

public sealed class SessionManagementEndpointTests
{
    [Fact]
    public void Session_endpoints_require_authenticated_sid_policy()
    {
        var controllerPolicy = typeof(MeSessionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal(AuthPolicies.AuthenticatedOnly, controllerPolicy.Policy);
        Assert.Equal(AuthPolicies.AuthenticatedOnly, typeof(AuthPolicies)
            .GetField(nameof(AuthPolicies.AuthenticatedOnly))!.GetValue(null));
    }

    [Fact]
    public void Session_summary_does_not_serialize_token_or_device_fingerprint_fields()
    {
        var summary = new SessionSummary(
            Guid.NewGuid(), "Chrome", null, "Mozilla/5.0", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(summary);

        Assert.DoesNotContain("DeviceFp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Auth_logout_is_origin_header_filtered_and_authenticated()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Logout))!;

        Assert.NotNull(method.GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Single(a => ((AuthorizeAttribute)a).Policy == AuthPolicies.AuthenticatedOnly));
        Assert.NotNull(method.GetCustomAttributes(typeof(ServiceFilterAttribute), true)
            .Single(a => ((ServiceFilterAttribute)a).ServiceType.Name == "OriginAndHeaderValidationFilter"));
    }
}
