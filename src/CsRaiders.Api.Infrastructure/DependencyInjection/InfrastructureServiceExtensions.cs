using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CsRaiders.Api.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register DbContext, repositories, external services, etc. here

        return services;
    }
}
