# Interceptors

## Overview
Houses Entity Framework Core interceptors providing cross-cutting persistence policies such as sensitive property redaction for database audit logs.

## Interface
- `[AuditSaveChangesInterceptor.cs:AuditSaveChangesInterceptor]` - EF Core `SaveChangesInterceptor` providing centralized recursive redaction of sensitive credential and secret fields in audit payloads.
- `[AuditSaveChangesInterceptor.cs:AuditSaveChangesInterceptor.Redact]` - Recursively parses a `JsonDocument` and replaces all sensitive property values with `"[REDACTED]"`.

## Invariants
- `[AuditSaveChangesInterceptor.cs:AuditSaveChangesInterceptor.RedactNode]` - Any JSON property matching denied keys (`password_hash`, `otp_hash`, `refresh_token_hash`, `previous_refresh_token_hash`, `token_hash`, `password`, `otp`, `refresh_token` case-insensitively) is replaced by `"[REDACTED]"`.

## Side Effects
- None

## Verification
- Run: `dotnet test`
