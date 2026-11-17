using System.Net;
using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="PasswordResetToken"/>. Mutation lives in <see cref="Extensions.PasswordResetTokenExtensions"/>.</summary>
public static class PasswordResetTokenFactory
{
    public static PasswordResetToken Issue(Guid userId, byte[] tokenHash, IPAddress? ip, DateTimeOffset now) => new()
    {
        UserId = userId, TokenHash = tokenHash, RequestedIp = ip, CreatedAt = now,
        ExpiresAt = now + AuthRules.PasswordResetLifetime
    };
}
