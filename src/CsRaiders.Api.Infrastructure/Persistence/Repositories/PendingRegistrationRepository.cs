using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class PendingRegistrationRepository(AppDbContext db) : IPendingRegistrationRepository
{
    public async Task<PendingRegistration?> FindByEmailAsync(Email email, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.PendingRegistrations.AsQueryable() : db.PendingRegistrations.AsNoTracking();
        return await query.FirstOrDefaultAsync(p => p.Email == email, ct);
    }

    public async Task AddAsync(PendingRegistration pending, CancellationToken ct = default)
    {
        await db.PendingRegistrations.AddAsync(pending, ct);
    }

    public void Update(PendingRegistration pending)
    {
        db.PendingRegistrations.Update(pending);
    }

    public async Task DeleteByEmailAsync(Email email, CancellationToken ct = default)
    {
        await db.PendingRegistrations
            .Where(p => p.Email == email)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        return await db.PendingRegistrations
            .Where(p => p.ExpiresAt < now)
            .ExecuteDeleteAsync(ct);
    }
}
