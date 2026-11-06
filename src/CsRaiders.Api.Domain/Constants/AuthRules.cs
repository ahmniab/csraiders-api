namespace CsRaiders.Api.Domain.Constants;

public static class AuthRules
{
    // Tokens and sessions (FR-63)
    public static readonly TimeSpan AccessTokenLifetime     = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshAbsoluteLifetime = TimeSpan.FromDays(30);
    public static readonly TimeSpan RefreshIdleLifetime     = TimeSpan.FromDays(14);
    public static readonly TimeSpan SessionTouchThrottle    = TimeSpan.FromMinutes(1);

    // Passwords (FR-60, NIST 800-63B)
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;

    // OTP (decision D7)
    public static readonly TimeSpan OtpLifetime       = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan OtpResendCooldown = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan OtpSendWindow     = TimeSpan.FromHours(1);
    public const int OtpMaxAttempts     = 5;
    public const int OtpMaxSendsPerHour = 5;

    // Password reset (FR-65)
    public static readonly TimeSpan PasswordResetLifetime = TimeSpan.FromMinutes(30);

    // Lockout (FR-64)
    public static readonly TimeSpan LoginFailureWindow    = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LoginLockoutDuration  = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LoginMaxDelay         = TimeSpan.FromSeconds(15);
    public const int LoginDelayAfterFailures   = 3;
    public const int LoginLockoutAfterFailures = 10;

    // Default for app_settings key session.multi_max_concurrent
    public const int DefaultMultiSessionCap = 3;
}
