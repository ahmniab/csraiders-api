# DependencyInjection

## Overview
Configures and registers Application-layer services, Mediator handlers, FluentValidation validators, deferred post-commit actions, and mediator pipeline behaviors (validation and transactional unit-of-work) into the ASP.NET Core dependency injection container.

## Interface
- `[ApplicationServiceExtensions.cs:ApplicationServiceExtensions.AddApplicationServices(IServiceCollection)]`: Extension method on `IServiceCollection` to register mediator, validators, pipeline behaviors, and application services.
- `[ApplicationServiceExtensions.cs:ValidationBehavior<TMessage, TResponse>]`: Mediator pipeline behavior executing FluentValidation validators sequentially on incoming requests.
- `[ApplicationServiceExtensions.cs:TransactionBehavior<TMessage, TResponse>]`: Mediator pipeline behavior wrapping request execution in an atomic database transaction and executing post-commit actions upon success.
- `[ApplicationServiceExtensions.cs:PostCommitActions]`: In-memory implementation of `IPostCommitActions` queueing and executing deferred actions after transaction commits.

## Invariants
- `[ApplicationServiceExtensions.cs:AddApplicationServices]`: Must return the incoming `IServiceCollection` instance for fluent configuration chaining.
- `[ApplicationServiceExtensions.cs:ValidationBehavior<TMessage, TResponse>]`: Throws `ValidationException` when any validator produces failures, halting the pipeline.
- `[ApplicationServiceExtensions.cs:TransactionBehavior<TMessage, TResponse>]`: Commits transaction and executes post-commit actions on success; clears actions and rolls back transaction on error.

## Side Effects
- `[ApplicationServiceExtensions.cs:AddApplicationServices]`: Mutates the DI container service descriptor collection (`services`).
- `[ApplicationServiceExtensions.cs:TransactionBehavior<TMessage, TResponse>]`: Begins, commits, or rolls back database transactions, and flushes database changes via `IApplicationDbContext.SaveChangesAsync`.

## Verification
- Run: `dotnet test`
