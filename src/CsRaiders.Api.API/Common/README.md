# Common

## Overview
Defines shared strongly-typed configuration option models for authentication, site origin, proxy routing, rate limiting, background email queuing, and cryptographic operations within the CsRaiders API.

## Interface
- `AuthOptions` root configuration options model (`[AuthOptions.cs:AuthOptions]`, `[AuthOptions.cs:AuthOptions.SectionName]`)
- `SiteOptions` site domain and origin settings (`[AuthOptions.cs:SiteOptions]`)
- `ProxyOptions` trusted forwarder and proxy addresses (`[AuthOptions.cs:ProxyOptions]`)
- `RateLimitOptions` client request threshold configuration (`[AuthOptions.cs:RateLimitOptions]`)
- `EmailQueueOptions` background dispatch capacity settings (`[AuthOptions.cs:EmailQueueOptions]`)
- `CryptoOptions` concurrency timeout settings for cryptographic routines (`[AuthOptions.cs:CryptoOptions]`)

## Invariants
None

## Side Effects
None

## Verification
- Run: `dotnet test`
