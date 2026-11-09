using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using FluentAssertions;

namespace CsRaiders.Api.Application.UnitTests.Auth;

public sealed class RegisterResendVerifyTests
{
    private static readonly Email Mail = Email.Create("student@example.com");
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private const string Hash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdA==$dGFn$ p=1";

    [Fact]
    public void Reissue_preserves_password_hash_and_replaces_code()
    {
        var pending = PendingRegistration.Start(Mail, "Student", Hash.Replace("$ p", "$p"), [1, 2], null, Now);
        var result = pending.Reissue("Student", [3, 4], Now.AddMinutes(2));

        result.Should().Be(OtpSendResult.Sent);
        pending.PasswordHash.Should().Be(Hash.Replace("$ p", "$p"));
        pending.OtpHash.Should().Equal(3, 4);
        pending.AttemptsLeft.Should().Be(AuthRules.OtpMaxAttempts);
    }

    [Fact]
    public void Verify_decrements_attempts_for_each_invalid_code_and_then_exhausts()
    {
        var pending = PendingRegistration.Start(Mail, "Student", Hash.Replace("$ p", "$p"), [9], null, Now);
        for (var attempt = 0; attempt < AuthRules.OtpMaxAttempts; attempt++)
            pending.Verify([8], Now).Should().Be(OtpVerifyResult.Invalid);

        pending.AttemptsLeft.Should().Be(0);
        pending.Verify([9], Now).Should().Be(OtpVerifyResult.NoAttemptsLeft);
    }

    [Fact]
    public void Verify_success_is_available_before_expiry_and_pending_can_be_purged()
    {
        var pending = PendingRegistration.Start(Mail, "Student", Hash.Replace("$ p", "$p"), [7], null, Now);
        pending.Verify([7], Now.AddMinutes(1)).Should().Be(OtpVerifyResult.Verified);
        pending.Verify([7], Now.AddMinutes(11)).Should().Be(OtpVerifyResult.Expired);
    }
}
