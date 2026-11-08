# DependencyInjection

## Overview
Provides dependency injection service configuration and registration methods for the infrastructure layer, binding Entity Framework Core PostgreSQL DbContext, system time abstraction, identity seeding, and data repository implementations to the application service container.

## Interface
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions]` - Static extension class for service collection registration.
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - Registers `AppDbContext`, `IApplicationDbContext`, `TimeProvider.System`, `IdentitySeeder`, and repository interfaces (`IUserRepository`, `IRoleRepository`, `ISessionRepository`, `ILoginAttemptRepository`, `IPendingRegistrationRepository`, `IPasswordResetTokenRepository`).

## Invariants
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - `ConnectionStrings:Default` configuration string must be present, throwing `InvalidOperationException` if not configured.
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - Time provider must be registered as singleton `TimeProvider.System`; direct calls to `DateTime.UtcNow` are forbidden.

## Side Effects
- `[InfrastructureServiceExtensions.cs:InfrastructureServiceExtensions.AddInfrastructureServices]` - Modifies the `IServiceCollection` service descriptor collection by adding database context, seeders, and repository service registrations.

## Verification
- Run: `dotnet test`
