
using System.Diagnostics;
using System.Net;
using CsRaiders.Api.Application.Common.Interfaces;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Application.Common.Abstractions;
using CsRaiders.Api.Application.Extensions;
using CsRaiders.Api.Application.Factories;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Policies;
using CsRaiders.Api.Domain.ValueObjects;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CsRaiders.Api.Application.Features.Auth.Commands.Login;

public sealed record LoginCommand(
    string Email,
    string Password,
    IPAddress? Ip,
    string DeviceFingerprint,
    string? UserAgent) : IRequest<LoginResult>;

public sealed record LoginResult(
    bool Succeeded,
    string? AccessToken = null,
    string? RefreshToken = null,
    Guid? SessionId = null,
    string? DeviceFingerprint = null,
    string? ErrorCode = null,
    TimeSpan? RetryAfter = null,
    bool CapacityExhausted = false)
{
    public static LoginResult Failure(string code, TimeSpan? retryAfter = null)
        => new(false, ErrorCode: code, RetryAfter: retryAfter);

    public static LoginResult Capacity()
        => new(false, ErrorCode: "capacity_exhausted", CapacityExhausted: true);
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
    IDistributedCache cache,
    IRoleRepository roles,
    IRbacInvalidationToken invalidation,
    ILogger<LoginCommandHandler> logger)
    : IRequestHandler<LoginCommand, LoginResult>
{
    private const string Invalid = "invalid_credentials";
    
    public async ValueTask<LoginResult> Handle(
        LoginCommand request,
        CancellationToken ct)
    {
        
        try
        {
            // var emailStart = Stopwatch.GetTimestamp();
            var email = Email.Create(request.Email);
            // Log("Email.Create", emailStart);
            var now = clock.UtcNow;

            // 1. Login gate
            var gate = await gates.EvaluateAsync(email, request.Ip, now, ct);

            if (gate.Status != LoginGateStatus.Open)
            {
                var result = LoginResult.Failure(Invalid, gate.RetryAfter(now));
                return result;
            }

            // 2. User lookup
            var user = await users.FindByEmailWithPermissionsAsync(email, track: true, ct: ct);

            // 3. Password verification (Argon2id)
            var verification = await passwords.VerifyAsync(
                request.Password,
                user?.PasswordHash,
                ct);

            if (verification.Outcome == HashOutcome.Unavailable)
            {
                return LoginResult.Capacity();
            }

            // 4. Invalid credentials
            if (user is null || !verification.IsValid || !user.CanAuthenticate())
            {
                var outcome = await gates.RegisterFailureAsync(
                    email, request.Ip, user?.Id, now, ct);

                if (outcome == LoginFailureOutcome.LockedOut)
                {
                    await audit.AddAsync(
                        "login_locked_out",
                        user?.Id,
                        email,
                        request.Ip,
                        createdAt: now,
                        ct: ct);

                }

                var nextGate = await gates.EvaluateAsync(
                    email, request.Ip, now, ct);

                var result = LoginResult.Failure(
                    Invalid,
                    nextGate.RetryAfter(now));

                return result;
            }

            // 5. Login lock
            await users.AcquireLoginLockAsync(user.Id, ct);

            // 6. Reset login gate
            await gates.ResetAsync(email, request.Ip, now, ct);

            // 7. Password rehash (only when required)
            if (verification.NeedsRehash)
            {
                var upgraded 
                    = await passwords.HashAsync(request.Password, ct);


                if (upgraded.Outcome == HashOutcome.Unavailable)
                    return LoginResult.Capacity();
                

                user.UpgradePasswordHash(upgraded.Hash!);
            }

            // 8. Authentication policy
            var hasMulti =
                user.PermissionOverrides.CanHaveMultiSessions()
                ?? user.Roles.CanHaveMultiSessions();


            // 9. Active sessions
            var activeSessions = await sessions.FindActiveByUserIdAsync(
                user.Id, now, ct: ct);

            var activeCount = activeSessions.Count;

            // 10. Session cap
            var cap = hasMulti? await settings.GetMultiSessionCapAsync(ct): 1;

            var decision = SessionPolicy.Decide(
                hasMulti, 
                activeCount, 
                cap);

            // 11. Apply session policy
            await ApplySessionPolicyAsync(
                user.Id,
                activeSessions,
                decision,
                now,
                ct);


            // 12. Create refresh token
            var refresh = tokens.CreateRefreshToken(user.Id, IdGenerator.New(), now);

            // Use the same session ID for the session and refresh token.
            // Generate it separately to avoid accidentally using two IDs.
            var sessionId = IdGenerator.New();

            // Recreate the refresh token with the actual session ID.
            // This replaces the first generation above.
            refresh = tokens.CreateRefreshToken(user.Id, sessionId, now);

            var refreshHash = tokens.HashRefreshToken(refresh);

            // 13. Create session

            var session = SessionFactory.Open(
                sessionId,
                user.Id,
                refreshHash,
                request.DeviceFingerprint,
                request.Ip,
                request.UserAgent,
                hasMulti,
                decision.EnforceSingleSession,
                now);


            await sessions.AddAsync(session, ct);

            // 14. Persist changes
            try
            {
                var tracked = db.ChangeTracker.Entries().ToList();

                var added = tracked.Count(x => x.State == EntityState.Added);
                var modified = tracked.Count(x => x.State == EntityState.Modified);
                var deleted = tracked.Count(x => x.State == EntityState.Deleted);

                logger.LogInformation(
                    "EF tracking: Total={Total}, Added={Added}, Modified={Modified}, Deleted={Deleted}",
                    tracked.Count, added, modified, deleted);
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueSessionViolation(ex))
            {

                db.Entry(session).State = EntityState.Detached;

                var retryActive = await sessions.FindActiveByUserIdAsync(
                    user.Id, now, ct: ct);

                var retryDecision = SessionPolicy.Decide(
                    hasMulti, retryActive.Count, cap);
                
                await ApplySessionPolicyAsync(
                    user.Id,
                    retryActive,
                    retryDecision,
                    now,
                    ct);
                
                await sessions.AddAsync(session, ct);

                await db.SaveChangesAsync(ct);
            }

            // 16. Load roles
            var assignedRoles = user.Roles.Count > 0
                ? await roles.FindByIdsAsync(
                    user.Roles.Select(r => r.RoleId), ct)
                : [];


            // 17. Resolve permissions
            var effectivePerms = EffectivePermissions.Resolve(
                assignedRoles,
                user.PermissionOverrides);
            
            // 18. Distributed cache
            await cache.SetObjectAsync(
                $"prems:{sessionId}",
                effectivePerms);
            
            // 19. Access token

            var accessToken = tokens.CreateAccessToken(
                user.Id,
                session.Id,
                effectivePerms,
                now);

            return new(
                true,
                accessToken,
                refresh,
                session.Id,
                request.DeviceFingerprint);
        }
        catch
        {
            throw;
        }
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
            await sessions.RevokeAllByUserIdAsync(
                userId,
                SessionRevokeReason.Superseded,
                now,
                ct);


            foreach (var session in activeSessions)
                await cache.RemoveAsync($"sid:{session.Id}", ct);
            
        }
        else if (decision.Action == SessionAction.RevokeOldest)
        {
            var oldest = activeSessions
                .Take(decision.RevokeCount)
                .ToArray();


            await sessions.RevokeOldestNAsync(
                userId,
                decision.RevokeCount,
                SessionRevokeReason.CapExceeded,
                now,
                ct);
            
            foreach (var session in oldest)
                await cache.RefreshAsync($"sid:{session.Id}", ct);

            
        }
    }

    private static bool IsUniqueSessionViolation(DbUpdateException exception)
    {
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            var sqlState = current.GetType()
                .GetProperty("SqlState")?.GetValue(current) as string;

            var constraint = current.GetType()
                .GetProperty("ConstraintName")?.GetValue(current) as string;

            if (sqlState == "23505" &&
                string.Equals(
                    constraint,
                    "uq_sessions_one_active",
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
    void Log(string operation, long start)
    {
        logger.LogInformation(
            "[LoginTiming] {Operation}: {ElapsedMs:F3} ms",
            operation,
            Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
