using System.Net;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="LoginAttempt"/>. Mutation lives in <see cref="Extensions.LoginAttemptExtensions"/>.</summary>
public static class LoginAttemptFactory
{
    public static LoginAttempt For(Email email, DateTimeOffset now) => new() { Email = email, UpdatedAt = now };

    public static LoginAttempt For(Email email, IPAddress? ip, DateTimeOffset now) =>
        new() { Email = email, LastIp = ip, UpdatedAt = now };
}
