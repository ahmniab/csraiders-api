using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class RoleRepository(AppDbContext db) : IRoleRepository
{
    public async Task<Role?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.Roles.AsQueryable() : db.Roles.AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<Role?> FindBySlugAsync(string slug, bool track = false, CancellationToken ct = default)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var query = track ? db.Roles.AsQueryable() : db.Roles.AsNoTracking();
        return await query.FirstOrDefaultAsync(r => r.Slug == normalizedSlug, ct);
    }

    public async Task<IReadOnlyList<Role>> FindAllAsync(CancellationToken ct = default)
    {
        return await db.Roles
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Role>> FindByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids as IReadOnlyCollection<Guid> ?? ids.ToList();
        return await db.Roles
            .AsNoTracking()
            .Where(r => idList.Contains(r.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken ct = default)
    {
        return await db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Role role, CancellationToken ct = default)
    {
        await db.Roles.AddAsync(role, ct);
    }

    public void Update(Role role)
    {
        db.Roles.Update(role);
    }
}
