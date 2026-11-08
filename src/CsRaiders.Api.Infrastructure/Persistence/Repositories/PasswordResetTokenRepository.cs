using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class PasswordResetTokenRepository(AppDbContext db) : IPasswordResetTokenRepository
{
    public async Task<PasswordResetToken?> FindByTokenHashAsync(byte[] tokenHash, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.PasswordResetTokens.AsQueryable() : db.PasswordResetTokens.AsNoTracking();
        return await query.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
    }

    public async Task<IReadOnlyList<PasswordResetToken>> FindActiveByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        return await db.PasswordResetTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.UsedAt == null && now < t.ExpiresAt)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task AddAsync(PasswordResetToken token, CancellationToken ct = default)
    {
        await db.PasswordResetTokens.AddAsync(token, ct);
    }

    public void Update(PasswordResetToken token)
    {
        db.PasswordResetTokens.Update(token);
    }

    public async Task InvalidateAllByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        await db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ExecuteUpdateAsync(setter => setter.SetProperty(t => t.UsedAt, now), ct);
    }
}
