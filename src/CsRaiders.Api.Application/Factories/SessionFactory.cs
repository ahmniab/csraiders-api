using System.Net;
using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="Session"/>. Mutation lives in <see cref="Extensions.SessionExtensions"/>.</summary>
public static class SessionFactory
{
    public static Session Open(Guid userId, byte[] refreshTokenHash, string deviceFp, IPAddress? ip, string? userAgent,
                               bool isStudentSession, bool enforceSingleSession, DateTimeOffset now)
        => Open(IdGenerator.New(), userId, refreshTokenHash, deviceFp, ip, userAgent, isStudentSession, enforceSingleSession, now);

    public static Session Open(
        Guid sessionId,
        Guid userId,
        byte[] refreshTokenHash,
        string deviceFp,
        IPAddress? ip,
        string? userAgent,
        bool isStudentSession,
        bool enforceSingleSession,
        DateTimeOffset now)
    {
        if (refreshTokenHash.Length != 32) throw new ArgumentException("Expected a SHA-256 hash.", nameof(refreshTokenHash));
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceFp);

        return new Session(sessionId)
        {
            UserId = userId,
            RefreshTokenHash = refreshTokenHash,
            DeviceFp = deviceFp,
            Ip = ip,
            UserAgent = userAgent is { Length: > 512 } ? userAgent[..512] : userAgent,
            IsStudentSession = isStudentSession,
            EnforceSingleSession = enforceSingleSession,
            CreatedAt = now,
            LastSeenAt = now,
            IdleExpiresAt = now + AuthRules.RefreshIdleLifetime,
            ExpiresAt = now + AuthRules.RefreshAbsoluteLifetime
        };
    }
}
