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
        foreach (var name in new[] { nameof(AuthController.Register), nameof(AuthController.VerifyOtp), nameof(AuthController.ResendOtp) })
            Assert.NotNull(controller.GetMethod(name)!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true).SingleOrDefault());
        var routes = controller.GetMethods().SelectMany(m => m.GetCustomAttributes(typeof(HttpPostAttribute), true)
            .Cast<HttpPostAttribute>().Select(a => a.Template ?? string.Empty)).ToArray();
        Assert.Equal(new[] { "change-password", "forgot-password", "login", "logout", "refresh", "register", "resend-otp", "reset-password", "verify-otp" }, routes.Order());
    }
}
