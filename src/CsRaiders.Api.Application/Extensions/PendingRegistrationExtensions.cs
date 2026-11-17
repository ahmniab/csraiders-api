using System.Security.Cryptography;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Extensions;

public static class PendingRegistrationExtensions
{
    /// <summary>Second register call for the same email: new OTP, subject to cooldown and the hourly cap.</summary>
    public static OtpSendResult Reissue(this PendingRegistration pending, string name, byte[] otpHash, DateTimeOffset now)
    {
        if (now < pending.ResendAvailableAt) return OtpSendResult.CooldownActive;

        if (now - pending.SendWindowStartedAt >= AuthRules.OtpSendWindow)
        {
            pending.SendWindowStartedAt = now;
            pending.SendCountWindow = 0;
        }
        if (pending.SendCountWindow >= AuthRules.OtpMaxSendsPerHour) return OtpSendResult.HourlyLimitReached;

        // A resend changes only the OTP state. The password hash captured at
        // the start of registration must remain untouched (SEC-16).
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        pending.Name = name.Trim();
        pending.OtpHash = otpHash;
        pending.AttemptsLeft = AuthRules.OtpMaxAttempts;
        pending.ExpiresAt = now + AuthRules.OtpLifetime;
        pending.ResendAvailableAt = now + AuthRules.OtpResendCooldown;
        pending.SendCountWindow++;
        return OtpSendResult.Sent;
    }

    /// <summary>
    /// The caller must persist a decremented AttemptsLeft even though the request is rejected (SEC-03).
    /// </summary>
    public static OtpVerifyResult Verify(this PendingRegistration pending, byte[] candidateOtpHash, DateTimeOffset now)
    {
        if (now >= pending.ExpiresAt) return OtpVerifyResult.Expired;
        if (pending.AttemptsLeft <= 0) return OtpVerifyResult.NoAttemptsLeft;

        if (CryptographicOperations.FixedTimeEquals(pending.OtpHash, candidateOtpHash)) return OtpVerifyResult.Verified;

        pending.AttemptsLeft--;
        return OtpVerifyResult.Invalid;
    }
}
