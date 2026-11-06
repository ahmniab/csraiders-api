using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Domain.ValueObjects;
using FluentAssertions;

namespace CsRaiders.Api.Domain.UnitTests;

public class AuthDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private const string Phc = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2hoYXNoaGFzaGhhc2hoYXNoaGFzaGhhc2g";
    private static byte[] Hash(byte b) => Enumerable.Repeat(b, 32).ToArray();
    private static Email Mail => Email.Create("Ann@Example.com");

    [Theory]                                                    // FR-61 / FR-62 decision table
    [InlineData(false, 0, 3, true,  SessionAction.None,         0)]
    [InlineData(false, 2, 3, true,  SessionAction.RevokeAll,    2)]
    [InlineData(true,  1, 3, false, SessionAction.None,         0)]
    [InlineData(true,  3, 3, false, SessionAction.RevokeOldest, 1)]
    public void Session_policy_decision_table(bool multi, int active, int cap, bool single, SessionAction action, int count)
        => SessionPolicy.Decide(multi, active, cap).Should().Be(new SessionDecision(single, action, count));

    [Fact]                                                      // FR-62.2: DENY wins over a role grant
    public void Deny_override_beats_role_grant()
    {
        var role = Role.Create("Staff", "staff", RoleScope.Staff, [PermissionCatalog.SessionMulti, PermissionCatalog.CourseRead]);
        var user = User.RegisterVerified(Mail, "Ann", Phc, Now);
        user.SetPermissionOverride(PermissionCatalog.SessionMulti, PermissionEffect.Deny, Guid.NewGuid(), Now);

        var effective = EffectivePermissions.Resolve([role], user.PermissionOverrides);

        effective.Should().Contain(PermissionCatalog.CourseRead).And.NotContain(PermissionCatalog.SessionMulti);
    }

    [Fact]                                                      // SEC-08
    public void Replaying_a_rotated_refresh_token_revokes_the_session()
    {
        var s = Session.Open(Guid.NewGuid(), Hash(1), "fp", null, null, true, true, Now);

        s.TryRotate(Hash(1), Hash(2), "fp", Now).Should().Be(RefreshOutcome.Rotated);
        s.TryRotate(Hash(1), Hash(3), "fp", Now).Should().Be(RefreshOutcome.ReuseDetected);

        s.RevokeReason.Should().Be(SessionRevokeReason.ReuseDetected);
        s.TryRotate(Hash(2), Hash(4), "fp", Now).Should().Be(RefreshOutcome.Inactive);   // RT2 is dead too
    }

    [Fact]                                                      // FR-64
    public void Tenth_failure_locks_and_the_lock_expires()
    {
        var a = LoginAttempt.For(Mail, Now);
        for (var i = 1; i <= 9; i++) a.RegisterFailure(Now.AddSeconds(i), null, null);

        a.RegisterFailure(Now.AddSeconds(10), null, null).Should().Be(LoginFailureOutcome.LockedOut);
        a.Evaluate(Now.AddMinutes(1)).Status.Should().Be(LoginGateStatus.Locked);

        a.RegisterFailure(Now.AddMinutes(20), null, null).Should().Be(LoginFailureOutcome.Recorded);   // fresh window
        a.FailedCount.Should().Be(1);
    }

    [Fact]                                                      // SEC-03
    public void Sixth_otp_attempt_is_rejected_even_if_correct()
    {
        var p = PendingRegistration.Start(Mail, "Ann", Phc, Hash(1), null, Now);

        for (var i = 0; i < 5; i++) p.Verify(Hash(9), Now).Should().Be(OtpVerifyResult.Invalid);

        p.Verify(Hash(1), Now).Should().Be(OtpVerifyResult.NoAttemptsLeft);
    }

    [Fact]                                                      // SEC-01 / Bug-1 guard
    public void Plaintext_passwords_cannot_reach_an_entity()
    {
        var act = () => User.RegisterVerified(Mail, "Ann", "hunter2", Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("password_not_hashed");

        typeof(PendingRegistration).GetProperties().Select(p => p.Name)
            .Should().NotContain("Password");
    }
}
