using System.Reflection;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Entities;
using Xunit;

namespace CsRaiders.Api.ArchitectureTests;

public sealed class EmailArchitectureTests
{
    [Fact]
    public void Domain_layer_has_no_dependency_on_MailKit_or_MimeKit()
    {
        var domainAssembly = typeof(EmailMessage).Assembly;
        var references = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => name.Contains("MailKit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("MimeKit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Application_layer_has_no_dependency_on_MailKit_or_MimeKit()
    {
        var applicationAssembly = typeof(IEmailTemplateService).Assembly;
        var references = applicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => name.Contains("MailKit", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.Contains("MimeKit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Application_layer_does_not_reference_Infrastructure()
    {
        var applicationAssembly = typeof(IEmailTemplateService).Assembly;
        var references = applicationAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => name.Equals("CsRaiders.Api.Infrastructure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Domain_layer_does_not_reference_Infrastructure()
    {
        var domainAssembly = typeof(EmailMessage).Assembly;
        var references = domainAssembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        Assert.DoesNotContain(references, name => name.Equals("CsRaiders.Api.Infrastructure", StringComparison.OrdinalIgnoreCase));
    }
}
