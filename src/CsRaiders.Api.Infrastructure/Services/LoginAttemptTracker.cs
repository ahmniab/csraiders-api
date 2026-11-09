using System.Net;
using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Interfaces.Security;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Infrastructure.Services;

public sealed class LoginAttemptTracker(ILoginAttemptRepository attempts) : ILoginAttemptTracker
{
    public async Task<LoginGate> EvaluateAsync(Email email, IPAddress? ip, DateTimeOffset now, CancellationToken ct = default)
    {
        var keyIp = NormalizeIp(ip);
        var row = await attempts.FindByEmailAndIpAsync(email, keyIp, track: true, ct);
        var gate = row?.Evaluate(now) ?? LoginGate.Open;

        // Distributed attacks do not lock every source IP, but are paced once the
        // rolling email-wide failure budget is exhausted.
        var total = await attempts.CountRecentFailuresByEmailAsync(email, now - AuthRules.LoginFailureWindow, ct);
        if (total >= 30 && gate.Status == LoginGateStatus.Open)
            gate = LoginGate.Throttled(now.AddSeconds(1));

        return gate;
    }

    public async Task<LoginFailureOutcome> RegisterFailureAsync(Email email, IPAddress? ip, Guid? userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var keyIp = NormalizeIp(ip);
        var row = await attempts.FindByEmailAndIpAsync(email, keyIp, track: true, ct);
        var isNew = row is null;
        if (row is null)
        {
            row = LoginAttempt.For(email, keyIp, now);
            await attempts.AddAsync(row, ct);
        }

        var outcome = row.RegisterFailure(now, keyIp, userId);
        if (!isNew) attempts.Update(row);
        return outcome;
    }

    public async Task ResetAsync(Email email, IPAddress? ip, DateTimeOffset now, CancellationToken ct = default)
    {
        var row = await attempts.FindByEmailAndIpAsync(email, NormalizeIp(ip), track: true, ct);
        if (row is null) return;
        row.Reset(now);
        attempts.Update(row);
    }

    private static IPAddress NormalizeIp(IPAddress? ip) => ip ?? IPAddress.Parse("0.0.0.0");
}
