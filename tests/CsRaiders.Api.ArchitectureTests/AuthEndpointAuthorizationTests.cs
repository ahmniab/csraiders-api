using CsRaiders.Api.API.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.ArchitectureTests;

public sealed class AuthEndpointAuthorizationTests
{
    [Fact]
    public void US1_endpoints_declare_anonymous_access_explicitly()
    {
        Assert.NotEmpty(typeof(AuthController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Equal(3, typeof(AuthController).GetMethods().Count(m => m.Name is "Register" or "VerifyOtp" or "ResendOtp"));
    }
}
