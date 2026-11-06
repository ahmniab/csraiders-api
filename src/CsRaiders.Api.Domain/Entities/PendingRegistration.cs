using System.Security.Cryptography;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

/// <summary>
/// Pre-verification record (FR-60). There is intentionally NO "Password" member. Only PasswordHash exists (Bug-1 guard).
/// </summary>
public sealed class PendingRegistration : Entity
{
    private PendingRegistration() { }

    public Email Email { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public byte[] OtpHash { get; private set; } = [];
    public int AttemptsLeft { get; private set; }
    public int SendCountWindow { get; private set; }
    public DateTimeOffset SendWindowStartedAt { get; private set; }   // added: needed to know when the hourly window resets
    public DateTimeOffset ResendAvailableAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? DeviceFp { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static PendingRegistration Start(Email email, string name, string passwordHash, byte[] otpHash,
                                            string? deviceFp, DateTimeOffset now)
    {
        var pending = new PendingRegistration
        {
            Email = email, DeviceFp = deviceFp, CreatedAt = now, SendWindowStartedAt = now, SendCountWindow = 1
        };
        pending.Apply(name, passwordHash, otpHash, now);
        return pending;
    }

    /// <summary>Second register call for the same email: new OTP, subject to cooldown and the hourly cap.</summary>
    public OtpSendResult Reissue(string name, string passwordHash, byte[] otpHash, DateTimeOffset now)
    {
        if (now < ResendAvailableAt) return OtpSendResult.CooldownActive;

        if (now - SendWindowStartedAt >= AuthRules.OtpSendWindow)
        {
            SendWindowStartedAt = now;
            SendCountWindow = 0;
        }
        if (SendCountWindow >= AuthRules.OtpMaxSendsPerHour) return OtpSendResult.HourlyLimitReached;

        Apply(name, passwordHash, otpHash, now);
        SendCountWindow++;
        return OtpSendResult.Sent;
    }

    /// <summary>
    /// The caller must persist a decremented AttemptsLeft even though the request is rejected (SEC-03).
    /// </summary>
    public OtpVerifyResult Verify(byte[] candidateOtpHash, DateTimeOffset now)
    {
        if (now >= ExpiresAt) return OtpVerifyResult.Expired;
        if (AttemptsLeft <= 0) return OtpVerifyResult.NoAttemptsLeft;

        if (CryptographicOperations.FixedTimeEquals(OtpHash, candidateOtpHash)) return OtpVerifyResult.Verified;

        AttemptsLeft--;
        return OtpVerifyResult.Invalid;
    }

    private void Apply(string name, string passwordHash, byte[] otpHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        PasswordHash = PasswordHashGuard.EnsureHashed(passwordHash);
        OtpHash = otpHash;
        AttemptsLeft = AuthRules.OtpMaxAttempts;
        ExpiresAt = now + AuthRules.OtpLifetime;
        ResendAvailableAt = now + AuthRules.OtpResendCooldown;
    }
}
