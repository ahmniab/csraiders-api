using System.Text.RegularExpressions;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CsRaiders.Api.Infrastructure.Persistence;

public sealed class EmailConverter : ValueConverter<Email, string>
{
    public EmailConverter() : base(e => e.Value, s => Email.Create(s)) { }
}

/// <summary>PendingRegistration -> "pending_registration"; CapExceeded -> "cap_exceeded".</summary>
public sealed class SnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string> where TEnum : struct, Enum
{
    public SnakeCaseEnumConverter() : base(v => ToDb(v), v => FromDb(v)) { }

    public static string ToDb(TEnum value) =>
        Regex.Replace(value.ToString(), "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    public static TEnum FromDb(string value) =>
        Enum.Parse<TEnum>(value.Replace("_", ""), ignoreCase: true);
}
