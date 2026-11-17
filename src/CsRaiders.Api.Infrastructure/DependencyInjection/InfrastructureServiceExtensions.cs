using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.EmailTemplates.Templates;
using CsRaiders.Api.Infrastructure.Persistence;
using CsRaiders.Api.Infrastructure.Persistence.Repositories;
using CsRaiders.Api.Infrastructure.Services;
using CsRaiders.Api.Infrastructure.Options;
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

        services.AddDistributedMemoryCache();
        // One non-pooled factory registration supplies both scoped AppDbContext instances
        // and IDbContextFactory<AppDbContext>. Avoid registering AddDbContext and a
        // pooled factory together: the pool is singleton but its options become scoped.
        services.AddDbContextFactory<AppDbContext>(o =>
            o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
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
        services.AddScoped<ILoginAttemptTracker, LoginAttemptTracker>();
        services.AddScoped<IPendingRegistrationRepository, PendingRegistrationRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<ISecurityEventRepository, SecurityEventRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();
        services.AddScoped<IDeadLetterEmailRepository, DeadLetterEmailRepository>();
        services.AddScoped<ISessionStatusReader, SessionRepository>();
        services.AddScoped<ISecurityAuditWriter, SecurityAuditWriter>();
        services.AddSingleton<IEmailQueue, ChannelEmailQueue>();
        services.AddSingleton<IEmailTemplateService, EmailTemplateService>();
        services.AddSingleton<ITemplatedEmailQueue, TemplatedEmailQueue>();
        services.AddSingleton<RegistrationOtpTemplate>();
        services.AddSingleton<PasswordResetTemplate>();
        services.AddSingleton<SecurityNoticeTemplate>();
        services.AddSingleton<IAuthMetricsRecorder, AuthMetricsRecorder>();
        // Email configuration and transport
        services.AddOptions<EmailOptions>()
            .Configure(options =>
            {
                var section = configuration.GetSection(EmailOptions.SectionName);
                if (section["DeliveryMode"] is { } dm) options.DeliveryMode = dm;

                var smtp = section.GetSection("Smtp");
                if (smtp["Host"] is { } host) options.Smtp.Host = host;
                if (int.TryParse(smtp["Port"], out var port)) options.Smtp.Port = port;
                if (bool.TryParse(smtp["UseSsl"], out var useSsl)) options.Smtp.UseSsl = useSsl;
                if (smtp["Username"] is { } user) options.Smtp.Username = user;
                if (smtp["Password"] is { } pass) options.Smtp.Password = pass;
                if (smtp["SenderEmail"] is { } senderEmail) options.Smtp.SenderEmail = senderEmail;
                if (smtp["SenderName"] is { } senderName) options.Smtp.SenderName = senderName;
                if (int.TryParse(smtp["TimeoutSeconds"], out var timeout)) options.Smtp.TimeoutSeconds = timeout;
            });

        var deliveryMode = configuration[$"{EmailOptions.SectionName}:DeliveryMode"] ?? "Console";
        if (string.Equals(deliveryMode, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, ConsoleEmailSender>();
        }
        services.AddHostedService<EmailSenderWorker>();
        services.AddHostedService<AuthCleanupWorker>();
        services.AddScoped<IPasswordHasher, Security.Argon2idPasswordHasher>();
        services.AddSingleton<IOtpGenerator, Security.HmacOtpGenerator>();
        services.AddSingleton<ITokenService, Security.TokenService>();
        services.AddScoped<IAppSettingsProvider, AppSettingsProvider>();
        services.AddSingleton<IRbacInvalidationToken, RbacInvalidationToken>();
        services.AddScoped<ISessionReconciliationService, SessionReconciliationService>();

        return services;
    }
}
