using System.Net;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Common.Models;

public sealed record SessionSummary(
    Guid Id,
    string DeviceFp,
    IPAddress? Ip,
    string? UserAgent,
    bool IsStudentSession,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    SessionRevokeReason? RevokeReason);
