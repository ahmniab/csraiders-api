using System.Net;
using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

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
    IPlatformClock clock,
    IApplicationDbContext db,
    ISessionCacheHook sessionCache)
    : IRequestHandler<LoginCommand, LoginResult>
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
        var activeSessions = await sessions.FindActiveByUserIdAsync(user.Id, now, ct: ct);
        var activeCount = activeSessions.Count;
        var cap = await settings.GetMultiSessionCapAsync(ct);
        var decision = SessionPolicy.Decide(hasMulti, activeCount, cap);
        await ApplySessionPolicyAsync(user.Id, activeSessions, decision, now, ct);

        var refresh = tokens.CreateRefreshToken();
        var session = Session.Open(user.Id, tokens.HashRefreshToken(refresh), request.DeviceFingerprint,
            request.Ip, request.UserAgent, policy.IsStudent,
            decision.EnforceSingleSession, now);
        await sessions.AddAsync(session, ct);

        // The advisory lock is the normal race-free path. The partial unique index is
        // still a last-resort backstop, so force the insert inside the transaction and
        // retry once after EF rolls back its savepoint.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueSessionViolation(ex))
        {
            db.Entry(session).State = EntityState.Detached;

            var retryActive = await sessions.FindActiveByUserIdAsync(user.Id, now, ct: ct);
            var retryDecision = SessionPolicy.Decide(hasMulti, retryActive.Count, cap);
            await ApplySessionPolicyAsync(user.Id, retryActive, retryDecision, now, ct);

            await sessions.AddAsync(session, ct);
            await db.SaveChangesAsync(ct);
        }

        return new(true, tokens.CreateAccessToken(user.Id, session.Id, now), refresh, session.Id, request.DeviceFingerprint);
    }

    private async Task ApplySessionPolicyAsync(
        Guid userId,
        IReadOnlyList<Session> activeSessions,
        SessionDecision decision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (decision.Action == SessionAction.RevokeAll)
        {
            await sessions.RevokeAllByUserIdAsync(userId, SessionRevokeReason.Superseded, now, ct);
            foreach (var session in activeSessions)
                sessionCache.Evict(session.Id);
        }
        else if (decision.Action == SessionAction.RevokeOldest)
        {
            var oldest = activeSessions.Take(decision.RevokeCount).ToArray();
            await sessions.RevokeOldestNAsync(userId, decision.RevokeCount, SessionRevokeReason.CapExceeded, now, ct);
            foreach (var session in oldest)
                sessionCache.Evict(session.Id);
        }
    }

    private static bool IsUniqueSessionViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            var sqlState = current.GetType().GetProperty("SqlState")?.GetValue(current) as string;
            var constraint = current.GetType().GetProperty("ConstraintName")?.GetValue(current) as string;
            if (sqlState == "23505" && string.Equals(constraint, "uq_sessions_one_active", StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
