using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.Infrastructure.Persistence.Repositories;
using CsRaiders.Api.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CsRaiders.Api.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddPooledDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<ITransactionManager, EfTransactionManager>();
        services.AddSingleton(TimeProvider.System);          // never read DateTime.UtcNow directly
        services.AddSingleton<IPlatformClock, SystemPlatformClock>();
        services.AddScoped<IdentitySeeder>();

        // Repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
        services.AddScoped<IPendingRegistrationRepository, PendingRegistrationRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<ISecurityEventRepository, SecurityEventRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();
        services.AddScoped<ISessionStatusReader, SessionRepository>();
        services.AddSingleton<ISessionCacheHook, MemorySessionCacheHook>();
        services.AddScoped<ISecurityAuditWriter, SecurityAuditWriter>();
        services.AddSingleton<IEmailQueue, ChannelEmailQueue>();
        services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        services.AddHostedService<EmailSenderWorker>();
        services.AddScoped<IPasswordHasher, CsRaiders.Api.Infrastructure.Security.Argon2idPasswordHasher>();
        services.AddSingleton<IOtpGenerator, CsRaiders.Api.Infrastructure.Security.HmacOtpGenerator>();
        services.AddSingleton<ITokenService, CsRaiders.Api.Infrastructure.Security.TokenService>();

        return services;
    }
}
