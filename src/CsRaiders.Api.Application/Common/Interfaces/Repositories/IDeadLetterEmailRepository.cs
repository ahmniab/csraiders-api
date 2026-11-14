using CsRaiders.Api.Domain.Entities;

namespace CsRaiders.Api.Application.Common.Interfaces.Repositories;

public interface IDeadLetterEmailRepository
{
    Task AddAsync(DeadLetterEmail deadLetter, CancellationToken ct = default);
    Task<IReadOnlyList<DeadLetterEmail>> GetRecentAsync(int count = 50, CancellationToken ct = default);
}
