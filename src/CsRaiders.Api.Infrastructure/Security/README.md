# Security

## Overview
Provides cryptographic security primitives, password hashing with pepper support, HMAC-based one-time password generation and validation, and JSON Web Token (JWT) issuance and validation for session management.

## Interface
- `[Argon2idPasswordHasher.cs:Argon2idPasswordHasher]` - Implements `IPasswordHasher` providing memory-hard password hashing via Argon2id with semaphore concurrency throttling, multi-version pepper derivation, and constant-time verification.
- `[HmacOtpGenerator.cs:HmacOtpGenerator]` - Implements `IOtpGenerator` producing 6-digit numeric OTPs and calculating/verifying HMAC-SHA256 integrity digests over email and OTP combinations.
- `[TokenService.cs:TokenService]` - Implements `ITokenService` issuing JWT access tokens with scalar permission bitmask claims, refresh tokens with embedded session and token identifiers, verifying refresh token signatures and lifetimes, and computing SHA-256 hashes of refresh tokens.

## Invariants
- `[Argon2idPasswordHasher.cs:Argon2idPasswordHasher.HashAsync]` - Enforces Argon2id parameters (19 MB memory, 2 iterations, parallelism 1) and throttles concurrency using `SemaphoreSlim` with timeout defaulting to 2 seconds (`Crypto:SemaphoreTimeoutSeconds`).
- `[Argon2idPasswordHasher.cs:Argon2idPasswordHasher.VerifyAsync]` - Uses `CryptographicOperations.FixedTimeEquals` for constant-time hash comparisons to mitigate timing side-channel attacks.
- `[HmacOtpGenerator.cs:HmacOtpGenerator.Verify]` - Performs constant-time comparison via `CryptographicOperations.FixedTimeEquals`.
- `[TokenService.cs:TokenService.CreateAccessToken]` - Generates JWT access tokens with 5-minute lifetime (`AuthRules.AccessTokenLifetime`) containing subject (`sub`), session (`sid`), and invariant base-10 decimal integer permissions bitmask (`perms`) claims.
- `[TokenService.cs:TokenService.TryValidateRefreshToken]` - Enforces HMAC-SHA256 signature algorithm, validates issuer and audience against `Site:Origin`, checks expiry with 30s clock skew, and validates `sub`, `sid`, and `jti` GUID claims.

## Side Effects
- `[Argon2idPasswordHasher.cs:Argon2idPasswordHasher.HashAsync]` - Acquires and releases concurrency semaphore lock (`SemaphoreSlim`).
- `[Argon2idPasswordHasher.cs:Argon2idPasswordHasher.VerifyAsync]` - Acquires and releases concurrency semaphore lock (`SemaphoreSlim`).

## Verification
- Run: `dotnet test`
