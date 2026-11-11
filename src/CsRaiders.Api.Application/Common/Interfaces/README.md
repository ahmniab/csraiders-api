# Interfaces

## Overview
Provides core application contracts and database abstractions, including the primary database context interface, transaction scoping, deferred post-commit execution, and child contracts for entity repositories and security subsystems.

## Interface
- `[IApplicationDbContext.cs:IApplicationDbContext]`: Primary database context abstraction exposing entity `DbSet` collections and unit-of-work persistence via `SaveChangesAsync`.
- `[IPostCommitActions.cs:IPostCommitActions]`: Contract for deferring side-effect delegates until transaction commit completes successfully.
- `[ITransactionManager.cs:ITransactionScope]`: Disposable transaction boundary abstraction supporting `CommitAsync` and `RollbackAsync`.
- `[ITransactionManager.cs:ITransactionManager]`: Transaction management abstraction for beginning atomic transaction scopes.
- [Repositories](./Repositories/README.md): Entity repository interfaces for domain entities and access control.
- [Security](./Security/README.md): Core security service abstractions for auth, hashing, token issuance, and session controls.

## Invariants
- `[IApplicationDbContext.cs:SaveChangesAsync]`: Must commit tracked database entity changes atomically or roll back on error.
- `[IPostCommitActions.cs:Run]`: Must execute all queued post-commit actions only after the active transaction successfully commits.
- `[ITransactionManager.cs:ITransactionScope.CommitAsync]`: Atomically commits the active database transaction scope or throws if commit fails.

## Side Effects
- `[IApplicationDbContext.cs:SaveChangesAsync]`: Flushes tracked entity mutations to the underlying relational database.
- `[IPostCommitActions.cs:Enqueue]`: Mutates in-memory queue of pending post-commit actions.
- `[IPostCommitActions.cs:Run]`: Executes queued actions which trigger deferred side effects.
- `[ITransactionManager.cs:BeginAsync]`: Starts a database transaction scope.
- `[ITransactionManager.cs:ITransactionScope.CommitAsync]`: Commits underlying database transaction.
- `[ITransactionManager.cs:ITransactionScope.RollbackAsync]`: Rolls back uncommitted database transaction.

## Verification
- Run: `dotnet test`
