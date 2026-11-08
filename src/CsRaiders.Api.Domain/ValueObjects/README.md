# ValueObjects

## Overview
Domain value objects and guards ensuring string normalization, RFC-compliant email formatting, and credential security invariants.

## Interface
- [Email.cs:Email]: Immutable normalized email value object created via factory validation (`Value`, `Create`, `ToString`).
- [PasswordHashGuard.cs:PasswordHashGuard.EnsureHashed]: Defensive guard validating that password hash strings adhere to the Argon2id PHC format.

## Invariants
- [Email.cs:Email.Create]: Email string is trimmed, converted to lower-case, validated against `MailAddress.TryCreate`, capped at 254 characters, and throws `DomainException("invalid_email")` if invalid.
- [PasswordHashGuard.cs:PasswordHashGuard.EnsureHashed]: Verifies prefix starts with `$argon2id$v=19$`; throws `DomainException("password_not_hashed")` without echoing input to prevent accidental plaintext persistence.

## Side Effects
- None

## Verification
- Run: `dotnet test`
