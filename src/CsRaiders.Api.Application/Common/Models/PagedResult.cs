namespace CsRaiders.Api.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, Guid? NextCursor, int TotalReturned);
