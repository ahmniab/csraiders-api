using System.Net;
using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Common.Models;

public sealed record SessionSummary(
    Guid Id,
    string DeviceLabel,
    IPAddress? Ip,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);
