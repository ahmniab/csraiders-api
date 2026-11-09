using System.Reflection;
using CsRaiders.Api.API.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CsRaiders.Api.IntegrationTests;

public sealed class LoginEndpointTests
{
    [Fact]
    public void Login_endpoint_is_exposed_as_post_auth_login()
    {
        var method = typeof(AuthController).GetMethod(nameof(AuthController.Login));
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<HttpPostAttribute>());
        Assert.Equal("login", method.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Login_request_contains_email_and_password()
    {
        Assert.NotNull(typeof(LoginRequest).GetProperty(nameof(LoginRequest.Email)));
        Assert.NotNull(typeof(LoginRequest).GetProperty(nameof(LoginRequest.Password)));
    }
}
