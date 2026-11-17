using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.Exceptions;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Application.Factories;

/// <summary>Creation logic for <see cref="User"/>. Mutation lives in <see cref="Extensions.UserExtensions"/>.</summary>
public static class UserFactory
{
    /// <summary>Created at the end of OTP verification (decision D3): the email is verified by definition.</summary>
    public static User RegisterVerified(Email email, string name, string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > 200) throw new DomainException("name_too_long", "Name is too long.");

        return new User
        {
            Email = email,
            Name = name.Trim(),
            PasswordHash = PasswordHashGuard.EnsureHashed(passwordHash),
            Status = UserStatus.Active,
            EmailVerifiedAt = now,
            PasswordChangedAt = now,
            CreatedAt = now
        };
    }
}
