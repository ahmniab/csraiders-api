using System.Net.Mail;
using CsRaiders.Api.Domain.Exceptions;

namespace CsRaiders.Api.Domain.ValueObjects;

public sealed record Email
{
    public string Value { get; }
    private Email(string value) => Value = value;

    public static Email Create(string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value) || value.Length > 254 ||
            !MailAddress.TryCreate(value, out var parsed) || parsed.Address != value)
            throw new DomainException("invalid_email", "The email address is not valid.");
        return new Email(value);
    }

    public override string ToString() => Value;
}
