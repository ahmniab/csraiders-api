using System.Net;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using Mediator;

namespace CsRaiders.Api.Application.Features.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password, IPAddress? Ip, string DeviceFingerprint, string? UserAgent) : IRequest<LoginResult>;

public sealed record LoginResult(bool Succeeded, string? AccessToken = null, string? RefreshToken = null,
    Guid? SessionId = null, string? DeviceFingerprint = null, string? ErrorCode = null,
    TimeSpan? RetryAfter = null, bool CapacityExhausted = false)
{
    public static LoginResult Failure(string code, TimeSpan? retryAfter = null) => new(false, ErrorCode: code, RetryAfter: retryAfter);
    public static LoginResult Capacity() => new(false, ErrorCode: "capacity_exhausted", CapacityExhausted: true);
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotNull();
        RuleFor(x => x.DeviceFingerprint).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoginCommandHandler(
    IUserRepository users,
    ILoginAttemptTracker gates,
    IPasswordHasher passwords,
    ITokenService tokens,
    ISessionRepository sessions,
    IAppSettingsProvider settings,
    ISecurityEventRepository audit,
    IPlatformClock clock) : IRequestHandler<LoginCommand, LoginResult>
{
    private const string Invalid = "invalid_credentials";

    public async ValueTask<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = Email.Create(request.Email);
        var now = clock.UtcNow;
        var gate = await gates.EvaluateAsync(email, request.Ip, now, ct);
        if (gate.Status != LoginGateStatus.Open)
            return LoginResult.Failure(Invalid, gate.RetryAfter(now));

        var user = await users.FindByEmailAsync(email, track: true, ct: ct);
        var verification = await passwords.VerifyAsync(request.Password, user?.PasswordHash, ct);
        if (verification.Outcome == HashOutcome.Unavailable) return LoginResult.Capacity();

        if (user is null || !verification.IsValid || !user.CanAuthenticate)
        {
            var outcome = await gates.RegisterFailureAsync(email, request.Ip, user?.Id, now, ct);
            if (outcome == LoginFailureOutcome.LockedOut)
                await audit.AddAsync("login_locked_out", user?.Id, email, request.Ip, createdAt: now, ct: ct);
            var nextGate = await gates.EvaluateAsync(email, request.Ip, now, ct);
            return LoginResult.Failure(Invalid, nextGate.RetryAfter(now));
        }

        await users.AcquireLoginLockAsync(user.Id, ct);
        await gates.ResetAsync(email, request.Ip, now, ct);

        if (verification.NeedsRehash)
        {
            var upgraded = await passwords.HashAsync(request.Password, ct);
            if (upgraded.Outcome == HashOutcome.Unavailable) return LoginResult.Capacity();
            user.UpgradePasswordHash(upgraded.Hash!);
        }

        var policy = await users.GetAuthenticationPolicyAsync(user.Id, ct);
        var hasMulti = policy.HasSessionMulti;
        var activeCount = await sessions.CountActiveByUserIdAsync(user.Id, now, ct);
        var cap = await settings.GetMultiSessionCapAsync(ct);
        var decision = SessionPolicy.Decide(hasMulti, activeCount, cap);
        if (decision.Action == SessionAction.RevokeAll)
            await sessions.RevokeAllByUserIdAsync(user.Id, SessionRevokeReason.Superseded, now, ct);
        else if (decision.Action == SessionAction.RevokeOldest)
            await sessions.RevokeOldestNAsync(user.Id, decision.RevokeCount, SessionRevokeReason.CapExceeded, now, ct);

        var refresh = tokens.CreateRefreshToken();
        var session = Session.Open(user.Id, tokens.HashRefreshToken(refresh), request.DeviceFingerprint,
            request.Ip, request.UserAgent, policy.IsStudent,
            decision.EnforceSingleSession, now);
        await sessions.AddAsync(session, ct);
        return new(true, tokens.CreateAccessToken(user.Id, session.Id, now), refresh, session.Id, request.DeviceFingerprint);
    }
}
