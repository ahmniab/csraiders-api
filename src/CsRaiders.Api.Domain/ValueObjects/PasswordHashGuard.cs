using CsRaiders.Api.Domain.Exceptions;

namespace CsRaiders.Api.Domain.ValueObjects;

/// <summary>
/// Last line of defence for FR-60 / Bug-1: an entity refuses anything that is not an Argon2id PHC string,
/// so a plaintext password can never be assigned to a password column by mistake.
/// </summary>
public static class PasswordHashGuard
{
    private const string Prefix = "$argon2id$v=19$";

    public static string EnsureHashed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new DomainException("password_not_hashed", "A password hash in Argon2id PHC format is required.");
        return value;   // never echo the value into the message
    }
}
