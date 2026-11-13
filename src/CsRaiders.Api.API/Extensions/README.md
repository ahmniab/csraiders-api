# Extensions

## Overview
Provides hosting, dependency injection, and middleware pipeline extension methods for the CsRaiders API. Configures Serilog logging, cryptographic key validation, authentication schemes, rate limiting partitions, proxy forwarding rules, Swagger documentation, and automated development database migrations.

## Interface
- `CsRaidersApiAppExtensions.UseCsRaidersApi(this WebApplication app)` configures the complete HTTP middleware pipeline, endpoint routing, and development database seeding (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`)
- `CsRaidersApiServiceExtensions.AddCsRaidersApi(this WebApplicationBuilder builder)` registers and validates all API services, authentication, rate limiting, and dependencies (`[CsRaidersApiServiceExtensions.cs:CsRaidersApiServiceExtensions.AddCsRaidersApi]`)

## Invariants
- In production environments, `Auth:PasswordPepper`, `Auth:OtpHmacKey`, and `Auth:JwtSigningKey` must each have a minimum length of 32 characters and `Site:Origin` must be configured; throws `InvalidOperationException` otherwise (`[CsRaidersApiServiceExtensions.cs:CsRaidersApiServiceExtensions.AddCsRaidersApi]`).
- Endpoints under `/api/v1/auth` are constrained to partitioned fixed-window rate limits: `POST /api/v1/auth/refresh` allows up to 100 requests per minute per IP (`refresh_{ip}`) to accommodate shared NAT bursts, while other `/api/v1/auth` endpoints permit up to 20 requests per minute per IP (`auth_{ip}`), returning HTTP 429 when exceeded (`[CsRaidersApiServiceExtensions.cs:CsRaidersApiServiceExtensions.AddCsRaidersApi]`).
- JWT authentication requires HMAC-SHA256 signatures validated against `Auth:JwtSigningKey`, matching `Site:Origin` as both issuer and audience, with a 30-second clock skew tolerance (`[CsRaidersApiServiceExtensions.cs:CsRaidersApiServiceExtensions.AddCsRaidersApi]`).
- Database migrations and test identity seeding (with student multi-session disabled) only execute when `app.Environment.IsDevelopment()` is true (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Middleware execution order strictly precedes controller invocation: ForwardedHeaders -> GlobalExceptionMiddleware -> SerilogRequestLogging -> RateLimiter -> (Dev Migrations/Swagger) -> HttpsRedirection -> Authentication -> SessionValidationMiddleware -> Authorization -> MapControllers (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).

## Side Effects
- Configures Serilog as the primary logging factory, writing to the standard console (`[CsRaidersApiServiceExtensions.cs:CsRaidersApiServiceExtensions.AddCsRaidersApi]`).
- Registers services, option bindings, authentication schemes, and rate limiting policies into `IServiceCollection` (`[CsRaidersApiServiceExtensions.cs:CsRaidersApiServiceExtensions.AddCsRaidersApi]`).
- Executes pending database migrations and writes seed identity records during development application startup (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).
- Hooks into `app.Lifetime.ApplicationStarted` to log listening server endpoints (`[CsRaidersApiAppExtensions.cs:CsRaidersApiAppExtensions.UseCsRaidersApi]`).

## Verification
- Run: `dotnet test`
