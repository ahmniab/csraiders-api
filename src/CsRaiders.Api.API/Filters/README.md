# Filters

## Overview
Provides action filters for HTTP request validation and cross-site request forgery (CSRF) protection. Enforces strict origin matching, custom header checks, and content type requirements on sensitive state-changing endpoints.

## Interface
- `OriginAndHeaderValidationFilter` asynchronous action filter enforcing origin, referer, custom header, and JSON content-type constraints (`[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter]`, `[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter.OnActionExecutionAsync]`)

## Invariants
- Request `Origin` header (or authority segment of `Referer`) must match configuration key `Site:Origin` (`[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter.OnActionExecutionAsync]`).
- Request header `X-Requested-With` must match `"csraiders"` exactly (`[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter.OnActionExecutionAsync]`).
- Request `Content-Type` must start with `"application/json"` (`[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter.OnActionExecutionAsync]`).
- Any header mismatch or missing required value terminates the request with HTTP 403 Forbidden and JSON code `forbidden` without invoking subsequent action filters or controller code (`[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter.OnActionExecutionAsync]`).

## Side Effects
- Short-circuits action pipeline and writes an HTTP 403 `ObjectResult` to the action context upon validation failure (`[OriginAndHeaderValidationFilter.cs:OriginAndHeaderValidationFilter.OnActionExecutionAsync]`).

## Verification
- Run: `dotnet test`
