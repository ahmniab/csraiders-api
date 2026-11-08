# Middleware

## Overview
Provides centralized HTTP pipeline exception handling for the CsRaiders API, intercepting uncaught exceptions, logging them, and returning uniform JSON error responses.

## Interface
- `GlobalExceptionMiddleware` class (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware]`)
- `InvokeAsync(HttpContext context)` method (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.InvokeAsync]`)

## Invariants
- Catches any unhandled `Exception` thrown down the pipeline (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.InvokeAsync]`).
- Sets HTTP response status code to 500 `InternalServerError` and `ContentType` to `application/json` (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.HandleExceptionAsync]`).
- Serializes the caught exception message into an `{ error = ex.Message }` JSON payload (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.HandleExceptionAsync]`).

## Side Effects
- Logs error message and exception details to `ILogger<GlobalExceptionMiddleware>` (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.InvokeAsync]`).
- Writes JSON payload directly to the HTTP response stream (`[GlobalExceptionMiddleware.cs:GlobalExceptionMiddleware.HandleExceptionAsync]`).

## Verification
- Run: `dotnet test`
