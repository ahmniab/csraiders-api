using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Common.Models;

public sealed record UserSummary(
    Guid Id,
    string Email,
    string Name,
    string? Phone,
    string? WhatsApp,
    string? AcademicYear,
    UserStatus Status,
    DateTimeOffset CreatedAt);
