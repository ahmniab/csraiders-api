namespace CsRaiders.Api.Domain.Enums;

public enum UserStatus { Active, Suspended, Deleted }

public enum PermissionEffect { Grant, Deny }

// [Proposal] spec lists a `scope` column on roles but never defines it.
// Used for the "Student can't receive staff permissions" rule (M0 T4.3).
public enum RoleScope { Student, Staff }

public enum SessionRevokeReason
{
    Superseded, CapExceeded, PolicyReduced, ReuseDetected,
    Logout, PasswordReset, AdminRevoked,
    AccountDisabled   // FR-63: suspension / deletion
}

public enum RefreshOutcome { Rotated, InvalidToken, Inactive, DeviceMismatch, ReuseDetected }
public enum OtpSendResult { Sent, CooldownActive, HourlyLimitReached }
public enum OtpVerifyResult { Verified, Invalid, Expired, NoAttemptsLeft }
public enum LoginGateStatus { Open, Throttled, Locked }
public enum LoginFailureOutcome { Recorded, Throttled, LockedOut }
public enum SessionAction { None, RevokeAll, RevokeOldest }
