# Services

## Overview
Provides utility services for constructing, managing, and expiring HTTP session and device fingerprint cookies across authentication lifecycles.

## Interface
- `SessionCookieWriter` implementation of `ISessionCookieWriter` generating session and device fingerprint cookies (`[SessionCookieWriter.cs:SessionCookieWriter]`)
- Cookie constants: `RefreshCookie` (`__Host-csraiders_rt`) and `DeviceCookie` (`csraiders_dfp`) (`[SessionCookieWriter.cs:SessionCookieWriter.RefreshCookie]`, `[SessionCookieWriter.cs:SessionCookieWriter.DeviceCookie]`)
- Methods: `GetOrCreateDeviceFingerprint`, `CreateLoginCookies`, `CreateRefreshCookie`, `CreateClearRefreshCookie`, `CreateClearDeviceCookie` (`[SessionCookieWriter.cs:SessionCookieWriter.GetOrCreateDeviceFingerprint]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateLoginCookies]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateRefreshCookie]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateClearRefreshCookie]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateClearDeviceCookie]`)

## Invariants
- Refresh token cookies use the `"__Host-csraiders_rt"` prefix and a 30-day lifetime (`[SessionCookieWriter.cs:SessionCookieWriter.RefreshCookie]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateRefreshCookie]`).
- Device fingerprint cookies use `"csraiders_dfp"` and a 365-day lifetime (`[SessionCookieWriter.cs:SessionCookieWriter.DeviceCookie]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateLoginCookies]`).
- If an incoming device fingerprint is null, empty, or whitespace, a new 32-byte cryptographically random Base64 string is generated (`[SessionCookieWriter.cs:SessionCookieWriter.GetOrCreateDeviceFingerprint]`).
- Cookie clearing descriptors specify an empty string value and zero max age (`[SessionCookieWriter.cs:SessionCookieWriter.CreateClearRefreshCookie]`, `[SessionCookieWriter.cs:SessionCookieWriter.CreateClearDeviceCookie]`).

## Side Effects
- Generates random bytes using `RandomNumberGenerator.GetBytes` when producing missing device fingerprints (`[SessionCookieWriter.cs:SessionCookieWriter.GetOrCreateDeviceFingerprint]`).

## Verification
- Run: `dotnet test`
