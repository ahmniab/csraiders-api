using CsRaiders.Api.Domain.Enums;

namespace CsRaiders.Api.Application.Common.Models;

public sealed record UserFilter(
    UserStatus? Status = null,
    Guid? RoleId = null,
    string? SearchTerm = null,
    Guid? CursorId = null,
    int Limit = 20);
