# DependencyInjection

## Overview
Provides dependency injection service configuration and registration methods for the infrastructure layer, binding Entity Framework Core PostgreSQL DbContext factory, system time and clock abstractions, identity seeding, data repositories, security components, audit writers, email dispatch queues, and background cleanup workers to the application service container.

## Interface
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions]` - Static extension class for service collection registration.
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - Registers `AppDbContext` via factory, `IApplicationDbContext`, `ITransactionManager`, `TimeProvider.System`, `IPlatformClock`, `IdentitySeeder`, repositories (`IUserRepository`, `IRoleRepository`, `ISessionRepository`, `ILoginAttemptRepository`, `ILoginAttemptTracker`, `IPendingRegistrationRepository`, `IPasswordResetTokenRepository`, `ISecurityEventRepository`, `IAppSettingRepository`, `ISessionStatusReader`), security and caching services (`ISessionCacheHook`, `ISecurityAuditWriter`, `IEmailQueue`, `IAuthMetricsRecorder`, `IEmailSender`, `IPasswordHasher`, `IOtpGenerator`, `ITokenService`, `IAppSettingsProvider`, `IRbacInvalidationToken`, `ISessionReconciliationService`), and hosted workers (`EmailSenderWorker`, `AuthCleanupWorker`).

## Invariants
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - `ConnectionStrings:Default` configuration string must be present, throwing `InvalidOperationException` if not configured.
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - Time provider must be registered as singleton `TimeProvider.System`; direct calls to `DateTime.UtcNow` are forbidden.

## Side Effects
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - Modifies the `IServiceCollection` service descriptor collection by adding database context factory, seeders, clock providers, repositories, security services, and background hosted workers.

## Verification
- Run: `dotnet test`
