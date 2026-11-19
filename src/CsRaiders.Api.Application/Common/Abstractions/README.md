# Abstractions

## Overview
Defines core abstractions and primitives for platform timing and HTTP session/device cookie serialization contracts.

## Interface
- `[IPlatformClock.cs:IPlatformClock]`: Platform clock abstraction providing current UTC timestamp (`UtcNow`).
- `[ISessionCookieWriter.cs:SessionCookie]`: Record encapsulating cookie name, value, and expiration max age.
- `[ISessionCookieWriter.cs:ISessionCookieWriter]`: Factory abstraction generating session cookies, refresh token cookies, device fingerprint cookies, and cookie clearing descriptors.
- `[IRequestStepTimer.cs:IRequestStepTimer]`: Scoped per-request step timer tracking a tree of pipeline step durations (HTTP middleware checkpoints and Mediator behavior steps) and emitting one "Request pipeline timing" summary log entry per request.
- `[IReadOnlyMessage.cs:IReadOnlyMessage]`: Marker interface for read-only mediator messages; `TransactionBehavior` skips the transaction and `SaveChanges` for them.

## Invariants
- `[IPlatformClock.cs:IPlatformClock.UtcNow]`: Must always return current UTC time.
- `[ISessionCookieWriter.cs:ISessionCookieWriter.GetOrCreateDeviceFingerprint]`: Preserves existing valid device fingerprint or creates a newly generated unique identifier.
- `[ISessionCookieWriter.cs:ISessionCookieWriter.CreateClearRefreshCookie]`: Must output a cookie configured with an immediate expiration date to clear the refresh token.
- `[IReadOnlyMessage.cs:IReadOnlyMessage]`: Must only be implemented by messages whose handlers never persist through the EF `DbContext` (external side effects such as `IDistributedCache` writes are unrelated).

## Side Effects
- None

## Verification
- Run: `dotnet test`
