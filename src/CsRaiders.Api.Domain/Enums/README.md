# Enums

## Overview
Enumerated types representing domain states, authentication outcomes, permission evaluation effects, role scopes, and session lifecycle decisions.

## Interface
- [AuthEnums.cs:UserStatus]: Account lifecycle states (`Active`, `Suspended`, `Deleted`).
- [AuthEnums.cs:PermissionEffect]: Permission override effect types (`Grant`, `Deny`).
- [AuthEnums.cs:RoleScope]: Role scope boundary categorizing roles into `Student` or `Staff`.
- [AuthEnums.cs:SessionRevokeReason]: Enumeration of reasons triggering session revocation.
- [AuthEnums.cs:RefreshOutcome]: Status results of refresh token rotation and token reuse detection.
- [AuthEnums.cs:OtpSendResult]: Status outcomes for dispatching or resending OTP codes.
- [AuthEnums.cs:OtpVerifyResult]: Result codes for verifying candidate OTP codes.
- [AuthEnums.cs:LoginGateStatus]: Gate evaluation states for brute-force login protection (`Open`, `Throttled`, `Locked`).
- [AuthEnums.cs:LoginFailureOutcome]: Outcome classifications when recording a failed login.
- [AuthEnums.cs:SessionAction]: Policy action directives for session concurrency enforcement (`None`, `RevokeAll`, `RevokeOldest`).

## Invariants
- [AuthEnums.cs:PermissionEffect.Deny]: When combined with grants, `Deny` explicitly overrides `Grant` in permission resolution policies.
- [AuthEnums.cs:RoleScope.Student]: Demarcates roles restricted to student-allowed permissions.

## Side Effects
- None

## Verification
- Run: `dotnet test`
