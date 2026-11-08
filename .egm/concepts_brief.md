# CS Raiders — Authentication & Session Management Architecture Brief

* **Author:** ahmniab
* **Date:** 2026-11-07
* **Status:** In Brainstorming & Review

--------------------------------------------------------------------------------

## Overview

This brief evaluates the authentication, session lifecycle, and authorization architecture for the CS Raiders backend. The central architectural constraint is operating without an external Redis cluster for Milestone 0, relying on PostgreSQL 16 as the authoritative datastore. The system pairs short-lived 15-minute access JWTs with 30-day rotating refresh tokens stored in HTTP-only cookies, enforcing single-session concurrency per user unless granted `session:multi`.

--------------------------------------------------------------------------------

## Problem Statement: Database Saturation on Request Hot Path

Mandating a synchronous PostgreSQL query on every authenticated HTTP request (via `SessionValidationMiddleware` consulting `sessions` by `sid`) creates a severe architectural contradiction:
1. **Negating JWT Utility:** Cryptographic signature verification exists specifically to validate identity without database I/O. Adding a database query to every request retains the token parsing overhead of JWTs while subjecting every API endpoint to database query latency and connection pool limits.
2. **Connection Pool Saturation:** In ASP.NET Core under moderate concurrent load (500–1,500 RPS across multiple browser tabs and background polling), issuing an unbuffered SQL query per request quickly exhausts connection pools (Npgsql default max is 100), causing thread starvation and cascading HTTP 500/503 timeouts across the API.
3. **Inconsistent Revocation Standards:** The system permits a 30-second stale window via `IMemoryCache` for user RBAC permissions, but forces 0-second staleness for session revocation.

--------------------------------------------------------------------------------

## Architectural Alternatives for Request Validation

### Option 1: Bounded In-Process Sliding Cache (`IMemoryCache`, 5–10s TTL)
* **Mechanism:** `SessionValidationMiddleware` queries an in-process memory cache for the session's active status (`session:status:{sid}`). On cache hit, the request bypasses the database. On cache miss, it queries PostgreSQL by indexed `id` (`WHERE revoked_at IS NULL AND expires_at > NOW()`) and caches the active boolean for 5–10 seconds with sliding expiration.
* **Benefits:** Eliminates 95%–99% of database read traffic for active user sessions. Requires zero additional infrastructure or background processes.
* **Trade-offs & Failure Conditions:** Introducing this creates a maximum 5–10 second delay before a revoked or superseded session is rejected on that specific pod. In a multi-replica setup without shared state, an admin revocation can take up to 10 seconds to propagate across all instances.

### Option 2: Event-Driven Cache Invalidation (PostgreSQL `LISTEN` / `NOTIFY`)
* **Mechanism:** In addition to an in-process cache, a persistent background service runs `LISTEN session_revocations;` on PostgreSQL. Whenever a session is revoked, superseded, or logged out, the database transaction executes `NOTIFY session_revocations, '<sid>'`. All active backend instances receive the event within milliseconds and immediately purge the session ID from memory.
* **Benefits:** Delivers near-zero revocation latency (1–5ms) across all instances while maintaining 100% in-memory read throughput on the HTTP request hot path.
* **Trade-offs & Failure Conditions:** Requires maintaining a persistent long-lived connection per API pod to PostgreSQL. If the listener connection drops or reconnects during a network partition, missed notifications must be handled by an automatic local cache flush.

### Option 3: Pure Stateless JWT with Short Expiration (5–15 Minutes, No DB Query)
* **Mechanism:** The access JWT is validated purely cryptographically in memory via HMAC-SHA256 signature and timestamp bounds. The database is never queried on ordinary API calls. Explicit session validity is only checked when refreshing the token via `POST /api/v1/auth/refresh`.
* **Benefits:** Maximum API throughput and lowest latency. Completely decouples API request execution from PostgreSQL connection pool health.
* **Trade-offs & Failure Conditions:** Revocation cannot take effect immediately for active access tokens; a revoked user retains access until the 15-minute JWT expires, unless a synchronized denylist is maintained.

### Option 4: Opaque Session Tokens with Read-Replica Offloading & PgBouncer
* **Mechanism:** Discard JWT access tokens entirely on the client, replacing them with a random 256-bit session token in an HTTP-only cookie. Point `SessionValidationMiddleware` at a dedicated PostgreSQL read replica through PgBouncer running in transaction pooling mode.
* **Benefits:** Eliminates JWT encoding/decoding and token size overhead. Fits traditional web session paradigms.
* **Trade-offs & Failure Conditions:** Does not reduce query volume; requires operational management of PgBouncer and database read replicas, increasing infrastructure overhead for Milestone 0.

--------------------------------------------------------------------------------

## Goals vs. Non-Goals

### In-Scope Goals
* **Cryptographic Defense:** Argon2id ($m=19456\text{ KiB}, t=2, p=1$) with a server-side pepper secret (`Auth:PasswordPepper`) and bounded concurrency semaphores to protect against CPU DoS.
* **Anti-Enumeration Protection:** Timing-safe responses with dummy Argon2id verifications for unknown email addresses.
* **Durable Brute-Force & Lockout Protection:** In-memory rate limiting plus durable PostgreSQL `login_attempts` tracking with progressive delay and 15-minute lockouts.
* **Single-Session Enforcement:** Concurrency serialization via atomic SQL CTE deactivations under `uq_sessions_one_active`.
* **Token Rotation with Replay Detection:** 15-minute JWTs and 30-day rotating refresh tokens in `__Host-csraiders_rt` cookies with double-submit CSRF headers.
* **Hot-Path Throughput Protection:** Decouple request authentication from synchronous PostgreSQL lookups using in-process caching or pub/sub invalidation.
* **Fine-Grained RBAC:** $( \text{Roles} \cup \text{Grants} ) \setminus \text{Denies}$ permission evaluation cached in memory for 30 seconds.

### Out-of-Scope Non-Goals
* Redis or external caching clusters for Milestone 0.
* Social logins / third-party OAuth2 providers.
* Passwordless / WebAuthn authentication.
* Storing role or permission arrays inside JWT claims.

--------------------------------------------------------------------------------

## Alternatives Considered

* **Redis Distributed Cache:** Rejected for Milestone 0 due to infrastructure complexity and operational overhead; PostgreSQL remains authoritative datastore.
* **Stateless JWT-Only Session Model (Without Revocation):** Rejected due to strict requirement to immediately supersede student sessions upon second device login.
* **Role-Based Authorization (`IsInRole`):** Rejected in favor of explicit permission evaluation with user-level overrides.
