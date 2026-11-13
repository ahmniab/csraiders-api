# Implementation Plan: Token Bitmask Authorization

**Branch**: `002-token-bitmask-authorization` | **Date**: 2026-11-12 | **Spec**: [specs/002-token-bitmask-authorization/spec.md](file:///mnt/projects/web/backend/dotnet/CsRaiders.Api/specs/002-token-bitmask-authorization/spec.md)

**Input**: Feature specification from `/specs/002-token-bitmask-authorization/spec.md` and design document from `/docs/designs/token_embedded_authorization_bitmask_claims.md`

## Summary

This feature eliminates database split queries and cache synchronization overhead on permission-protected endpoints by embedding user effective permissions directly into signed JWT access tokens as a compact 64-bit unsigned bitmask (`perms`), serialized as an invariant base-10 decimal integer string. 

`PermissionAuthorizationHandler` evaluates incoming requests purely in memory in sub-microseconds (< 50 ns) via bitwise operations (`(bitmask & requiredBit) != 0UL`) with zero I/O, zero database queries, and zero memory cache lookups. Access token lifetime is bounded to 5 minutes so ordinary permission updates propagate automatically without ending user sessions. For immediate containment of high-risk actions, removing any designated sensitive capability (`role:manage`, `payment:refund`, `user:write`, `session:revoke`) triggers active session revocation within 5 seconds via `SessionValidationMiddleware`. 

Tokens lacking the `"perms"` claim are rejected immediately with zero fallback. Granular string-based permissions are preserved in PostgreSQL to eliminate database migrations and bit-shift drift corruption. To protect the database from write contention and query storms during 5-minute token rotations, `RefreshTokenCommandHandler` enforces a fail-fast session rotation sequence and leverages an in-memory bitmask cache, shielding PostgreSQL from multi-table joins on 99%+ of refresh operations while partitioning rate limits to 100 req/min on `/api/v1/auth/refresh` for shared NAT resilience.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0 (`net10.0`)

**Primary Dependencies**: ASP.NET Core 10 (`Microsoft.AspNetCore.App`), `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.14, EntityFramework Core 10 (`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3), `Mediator` 3.0.1, `System.IdentityModel.Tokens.Jwt` 8.14.0

**Storage**: PostgreSQL (existing tables `roles`, `user_roles`, `user_permissions`, `users`, `sessions` remain unchanged; zero schema migrations) + `IMemoryCache` for in-process bitmask memoization on token refresh

**Testing**: xUnit 2.9.3, FluentAssertions 6.12.2, Microsoft.NET.Test.Sdk 17.14.1

**Target Platform**: Linux server / containerized web service (Kestrel)

**Project Type**: Web API backend service (`Microsoft.NET.Sdk.Web`)

**Performance Goals**:
- Sub-microsecond (< 50 ns) permission checks in `PermissionAuthorizationHandler` via bitwise operations.
- Zero heap allocations on the authorization check hot path.
- Compact access tokens (< 280 bytes), avoiding reverse proxy header limits (`HTTP 431`).
- $\ge 99\%$ of token refreshes avoid database queries against `users` and `roles`.

**Constraints**:
- Access tokens expire strictly after 5 minutes (+ 30 seconds clock skew tolerance).
- Bitmask formatted as an invariant unsigned base-10 decimal integer string (`"perms"`) to prevent JavaScript 53-bit float precision truncation.
- Zero database schema migrations; retain string-based permission storage and audit metadata in PostgreSQL.
- Removing sensitive permissions (`role:manage`, `payment:refund`, `user:write`, `session:revoke`) terminates active user sessions within 5 seconds.
- Partitioned rate limiting on `/api/v1/auth/refresh` at 100 req/min per IP, while maintaining 20 req/min on other auth endpoints.
- Strict fail-fast rejection of legacy access tokens lacking `perms`; zero dual-read fallback.

**Scale/Scope**: 20 granular permissions (bounded by 64 bits), 4 projects modified in `CsRaiders.Api.sln` (`Domain`, `Application`, `Infrastructure`, `API`), plus unit and integration test suites.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The project constitution at `.specify/memory/constitution.md` currently contains default unratified template placeholders. The design adheres strictly to the architectural standards established across the codebase:
- **Clean Architecture & Boundary Integrity**: Domain constants and mapping logic (`PermissionBits`, `PermissionBitmaskMapper`) reside in `CsRaiders.Api.Domain` with zero infrastructure dependencies. Security interfaces (`ITokenService`) reside in `CsRaiders.Api.Application`. Token serialization resides in `CsRaiders.Api.Infrastructure`. Authorization policies and middleware reside in `CsRaiders.Api.API`.
- **Fail-Fast Security**: Rejects invalid, missing, or malformed claims immediately without expensive fallback queries.
- **Zero Database Migration**: Preserves PostgreSQL schemas unchanged, maintaining operational simplicity and preventing deployment locking.
- **TDD & Testability**: Domain mapping, application commands (login, refresh), and authorization handlers are fully covered with isolated unit and integration test suites.

*Gate Status*: **PASSED**. No constitutional violations or unwarranted complexity detected.

## Project Structure

### Documentation (this feature)

```text
specs/002-token-bitmask-authorization/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── jwt-access-token-claims.md
│   ├── auth-refresh-api.md
│   └── permission-authorization-policy.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── CsRaiders.Api.Domain/
│   ├── Constants/
│   │   ├── PermissionBits.cs             # [NEW] 64-bit flags enum mapping all 20 catalog permissions
│   │   ├── PermissionBitmaskMapper.cs     # [NEW] Bidirectional string <-> ulong bitmask utility
│   │   ├── PermissionCatalog.cs          # Canonical list of 20 permission names
│   │   └── AuthRules.cs                  # [MODIFY] AccessTokenLifetime changed from 15m to 5m
│   └── Policies/
│       └── EffectivePermissions.cs       # Resolves role permissions + overrides
├── CsRaiders.Api.Application/
│   ├── Common/Interfaces/Security/
│   │   └── ITokenService.cs              # [MODIFY] CreateAccessToken accepts ulong permissionsBitmask
│   └── Features/
│       ├── Auth/Commands/
│       │   ├── Login/LoginCommand.cs     # [MODIFY] Resolves permissions -> bitmask, populates cache, issues token
│       │   └── Refresh/RefreshTokenCommand.cs # [MODIFY] Fail-fast rotation, bitmask memoization, token issuance
│       └── Admin/
│           └── RbacCommands.cs           # [MODIFY] Revokes active sessions on sensitive permission loss
├── CsRaiders.Api.Infrastructure/
│   └── Security/
│       └── TokenService.cs               # [MODIFY] Encodes ulong bitmask as decimal string "perms" claim
└── CsRaiders.Api.API/
    ├── Authorization/
    │   ├── PermissionAuthorizationHandler.cs # [MODIFY] Pure in-memory bitwise check (< 50 ns), zero I/O
    │   └── HasPermissionAttribute.cs     # [MODIFY] Updated docstring
    └── Extensions/
        └── CsRaidersApiServiceExtensions.cs # [MODIFY] 100 req/min rate limit partition on refresh route

tests/
├── CsRaiders.Api.Domain.UnitTests/
│   └── PermissionBitmaskMapperTests.cs   # [NEW] Tests for bitmask mapping, edge cases, and flags
├── CsRaiders.Api.Application.UnitTests/
│   └── Auth/
│       ├── LoginCommandHandlerTests.cs   # [NEW] Tests for login bitmask resolution & cache population
│       └── RefreshTokenCommandHandlerTests.cs # [NEW] Tests for fail-fast refresh & bitmask memoization
└── CsRaiders.Api.IntegrationTests/
    └── PermissionAuthorizationHandlerTests.cs # [NEW] Tests for HTTP bitmask authorization pipeline
```

**Structure Decision**: Standard 4-tier Clean Architecture / Vertical Slice layout matching the existing solution structure. No new projects or structural reorganizations required.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No constitutional violations exist. Design simplifications include:*
- Rejecting legacy token dual-read fallback eliminates complex caching and query fallbacks in `PermissionAuthorizationHandler`.
- Retaining string storage in PostgreSQL eliminates database migrations, data locking, and bit-drift corruption risks.
- Encoding bitmasks as decimal strings eliminates float-truncation risks in JavaScript clients while enabling zero-allocation parsing in .NET.
