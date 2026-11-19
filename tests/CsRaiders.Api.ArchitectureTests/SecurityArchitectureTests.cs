using System.Reflection;
using CsRaiders.Api.API.Authorization;
using CsRaiders.Api.API.Controllers;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.API.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CsRaiders.Api.ArchitectureTests;

public sealed class SecurityArchitectureTests
{
    [Fact]
    public void Every_mapped_endpoint_declares_explicit_authorization()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(ControllerBase)));
        foreach (var controller in controllers)
        {
            var controllerHasPolicy = HasAuthorization(controller.GetCustomAttributes(true));
            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Any()))
            {
                Assert.True(controllerHasPolicy || HasAuthorization(method.GetCustomAttributes(true)),
                    $"{controller.Name}.{method.Name} must declare AllowAnonymous or Authorize.");
            }
        }
    }

    [Fact]
    public void Domain_has_no_project_references()
    {
        var forbidden = typeof(Entity).Assembly.GetReferencedAssemblies()
            .Where(x => x.Name?.StartsWith("CsRaiders.Api.", StringComparison.Ordinal) == true)
            .Select(x => x.Name).ToArray();
        Assert.Empty(forbidden);
    }

    [Fact]
    public void No_role_based_authorization_attributes_are_present()
    {
        var controllers = typeof(AuthController).Assembly.GetTypes();
        var roleAttributes = controllers.SelectMany(t => t.GetCustomAttributes(true).Concat(t.GetMethods().SelectMany(m => m.GetCustomAttributes(true))))
            .OfType<AuthorizeAttribute>().Where(a => !string.IsNullOrWhiteSpace(a.Roles));
        Assert.Empty(roleAttributes);
    }

    [Fact]
    public void Session_validation_hot_path_does_not_inject_db_context()
    {
        var parameters = typeof(SessionValidationMiddleware).GetConstructors().Single().GetParameters();
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(AppDbContext));
    }

    private static bool HasAuthorization(IEnumerable<object> attributes) =>
        attributes.OfType<AllowAnonymousAttribute>().Any() ||
        attributes.OfType<AuthorizeAttribute>().Any();
}
