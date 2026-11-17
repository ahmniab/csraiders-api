using CsRaiders.Api.API.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace CsRaiders.Api.ArchitectureTests;

public sealed class AuthEndpointAuthorizationTests
{
    [Fact]
    public void US1_endpoints_declare_anonymous_access_explicitly()
    {
        foreach (var name in new[]
                 {
                     nameof(AuthController.Register), 
                     nameof(AuthController.VerifyOtp), 
                     nameof(AuthController.ResendOtp)
                 })
            Assert.NotNull(typeof(AuthController)
                .GetMethod(name)!
                .GetCustomAttributes(typeof(AllowAnonymousAttribute), true)
                .SingleOrDefault());
        Assert.Equal(3, typeof(AuthController)
            .GetMethods()
            .Count(m => m.Name is "Register" or "VerifyOtp" or "ResendOtp"));
    }
}
