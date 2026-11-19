# Middleware

## Overview
Provides centralized HTTP pipeline exception handling and active session validation for the CsRaiders API. Intercepts uncaught exceptions to return uniform JSON error responses, and enforces real-time session revocation and sliding expiration checks against a short-lived thread-safe memory cache.

## Interface
- `GlobalExceptionMiddleware` exception handling middleware intercepting unhandled errors (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware]`, `[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.InvokeAsync]`)
- `SessionValidationMiddleware` session status and revocation verification middleware (`[SessionValidationMiddleware.cs:SessionValidationMiddleware]`, `[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`)
- `PipelineTimingMiddleware.UseTimedStep` timing checkpoint middleware measuring the middleware registered directly after it and aggregating per-step durations into the request pipeline timing summary (`[PipelineTimingMiddleware.cs:UseTimedStep]`)

## Invariants
- Catches any unhandled `Exception` thrown down the pipeline (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.InvokeAsync]`).
- Maps `ValidationException` and `DomainException` to HTTP 422 Unprocessable Entity with corresponding error codes, defaulting other unhandled exceptions to HTTP 500 Internal Server Error with code `internal_error` (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.HandleExceptionAsync]`).
- Sets HTTP response `ContentType` to `application/json` on unhandled exceptions and serializes error details into an `{ error = { code, message } }` payload (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.HandleExceptionAsync]`).
- Bypasses session validation if the request is unauthenticated or lacks a valid GUID `sid` claim (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).
- Session statuses are cached in-memory for 5 seconds per session ID (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).
- Inactive, expired, idle-expired, or revoked sessions immediately evict cache entries, delete the `__Host-csraiders_rt` refresh cookie, and terminate requests with HTTP 401 Unauthorized and code `session_superseded` (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).

## Side Effects
- Logs error message and exception details to `ILogger<GlobalExceptionMiddleware>` (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.InvokeAsync]`).
- Emits one `Request pipeline timing` information log entry per request via the scoped `IRequestStepTimer`, with inclusive/exclusive durations for every `UseTimedStep` checkpoint and Mediator pipeline behavior step (`[PipelineTimingMiddleware.cs:UseTimedStep]`).
- Writes JSON error payloads directly to the HTTP response stream (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.HandleExceptionAsync]`, `[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).
- Increments `session_cache.hit` and `session_cache.miss` metrics via `IAuthMetricsRecorder.Increment` (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).
- Queries session status via `ISessionStatusReader.ReadAsync` on cache misses (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).
- Evicts revoked session tokens from `ISessionCacheHook` and internal cache dictionary (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).
- Deletes `__Host-csraiders_rt` response cookie on failed session validation (`[SessionValidationMiddleware.cs:SessionValidationMiddleware.InvokeAsync]`).

## Verification
- Run: `dotnet test`
