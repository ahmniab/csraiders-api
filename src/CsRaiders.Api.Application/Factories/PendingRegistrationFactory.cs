using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="PendingRegistration"/>. Mutation lives in <see cref="Extensions.PendingRegistrationExtensions"/>.</summary>
public static class PendingRegistrationFactory
{
    public static PendingRegistration Start(Email email, string name, string passwordHash, byte[] otpHash,
                                            string? deviceFp, DateTimeOffset now)
    {
        var pending = new PendingRegistration
        {
            Email = email, DeviceFp = deviceFp, CreatedAt = now, SendWindowStartedAt = now, SendCountWindow = 1
        };
        Apply(pending, name, passwordHash, otpHash, now);
        return pending;
    }

    private static void Apply(PendingRegistration p, string name, string passwordHash, byte[] otpHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        p.Name = name.Trim();
        p.PasswordHash = PasswordHashGuard.EnsureHashed(passwordHash);
        p.OtpHash = otpHash;
        p.AttemptsLeft = AuthRules.OtpMaxAttempts;
        p.ExpiresAt = now + AuthRules.OtpLifetime;
        p.ResendAvailableAt = now + AuthRules.OtpResendCooldown;
    }
}
