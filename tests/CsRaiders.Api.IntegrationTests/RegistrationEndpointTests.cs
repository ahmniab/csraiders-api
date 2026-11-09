using CsRaiders.Api.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.IntegrationTests;

public sealed class RegistrationEndpointTests
{
    [Fact]
    public void Auth_surface_is_anonymous_and_has_registration_and_login_routes()
    {
        var controller = typeof(AuthController);
        Assert.NotEmpty(controller.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        var routes = controller.GetMethods().SelectMany(m => m.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>().Select(a => a.Template ?? string.Empty)).ToArray();
        Assert.Equal(new[] { "login", "register", "resend-otp", "verify-otp" }, routes.Order());
    }
}
