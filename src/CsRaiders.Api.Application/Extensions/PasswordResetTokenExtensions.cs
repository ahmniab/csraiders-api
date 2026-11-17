using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Extensions;

public static class PasswordResetTokenExtensions
{
    public static bool IsUsable(this PasswordResetToken token, DateTimeOffset now)
        => token.UsedAt is null && now < token.ExpiresAt;

    /// <summary>Single use (FR-65).</summary>
    public static bool TryConsume(this PasswordResetToken token, DateTimeOffset now)
    {
        if (!token.IsUsable(now)) return false;
        token.UsedAt = now;
        return true;
    }

    /// <summary>Used when a newer reset is requested: older unused tokens die.</summary>
    public static void Invalidate(this PasswordResetToken token, DateTimeOffset now) => token.UsedAt ??= now;
}
