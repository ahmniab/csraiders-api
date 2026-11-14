using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class DeadLetterEmailRepository(AppDbContext db) : IDeadLetterEmailRepository
{
    public async Task AddAsync(DeadLetterEmail deadLetter, CancellationToken ct = default)
    {
        await db.DeadLetterEmails.AddAsync(deadLetter, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeadLetterEmail>> GetRecentAsync(int count = 50, CancellationToken ct = default)
    {
        return await db.DeadLetterEmails
            .AsNoTracking()
            .OrderByDescending(x => x.FailedAt)
            .Take(count)
            .ToListAsync(ct);
    }
}
