using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using System.Net;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class LoginAttemptRepository(AppDbContext db) : ILoginAttemptRepository
{
    public async Task<LoginAttempt?> FindByEmailAsync(Email email, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.LoginAttempts.AsQueryable() : db.LoginAttempts.AsNoTracking();
        return await query.FirstOrDefaultAsync(a => a.Email == email, ct);
    }

    public async Task<LoginAttempt?> FindByEmailAndIpAsync(Email email, IPAddress? ip, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.LoginAttempts.AsQueryable() : db.LoginAttempts.AsNoTracking();
        return await query.FirstOrDefaultAsync(a => a.Email == email && a.LastIp == ip, ct);
    }

    public Task<int> CountRecentFailuresByEmailAsync(Email email, DateTimeOffset since, CancellationToken ct = default) =>
        db.LoginAttempts.Where(a => a.Email == email && a.FirstFailureAt >= since)
            .SumAsync(a => a.FailedCount, ct);

    public async Task AddAsync(LoginAttempt attempt, CancellationToken ct = default)
    {
        await db.LoginAttempts.AddAsync(attempt, ct);
    }

    public void Update(LoginAttempt attempt)
    {
        db.LoginAttempts.Update(attempt);
    }

    public async Task ResetByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        await db.LoginAttempts
            .Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(setter => setter
                .SetProperty(a => a.FailedCount, 0)
                .SetProperty(a => a.FirstFailureAt, (DateTimeOffset?)null)
                .SetProperty(a => a.DelayUntil, (DateTimeOffset?)null)
                .SetProperty(a => a.LockedUntil, (DateTimeOffset?)null)
                .SetProperty(a => a.UpdatedAt, now), ct);
    }

    public async Task<int> DeleteStaleAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        return await db.LoginAttempts
            .Where(a => a.UpdatedAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }
}
