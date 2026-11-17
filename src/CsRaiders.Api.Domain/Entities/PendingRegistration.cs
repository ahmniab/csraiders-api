using CsRaiders.Api.Domain.Common;
using CsRaiders.Api.Domain.ValueObjects;

namespace CsRaiders.Api.Domain.Entities;

/// <summary>
/// Pre-verification record (FR-60). There is intentionally NO "Password" member. Only PasswordHash exists (Bug-1 guard).
/// </summary>
public sealed class PendingRegistration : Entity
{
    public Email Email { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public byte[] OtpHash { get; set; } = [];
    public int AttemptsLeft { get; set; }
    public int SendCountWindow { get; set; }
    public DateTimeOffset SendWindowStartedAt { get; set; }   // added: needed to know when the hourly window resets
    public DateTimeOffset ResendAvailableAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string? DeviceFp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
