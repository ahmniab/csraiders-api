# DependencyInjection

## Overview
Configures and registers Application-layer services and dependencies into the ASP.NET Core dependency injection container.

## Interface
- `[ApplicationServiceExtensions.cs:ApplicationServiceExtensions.AddApplicationServices(IServiceCollection)]`: Extension method on `IServiceCollection` to register application services.

## Invariants
- `[ApplicationServiceExtensions.cs:AddApplicationServices]`: Must return the incoming `IServiceCollection` instance for fluent configuration chaining.

## Side Effects
- `[ApplicationServiceExtensions.cs:AddApplicationServices]`: Mutates the DI container service descriptor collection (`services`).

## Verification
- Run: `dotnet test`
