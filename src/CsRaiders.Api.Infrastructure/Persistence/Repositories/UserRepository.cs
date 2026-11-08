using CsRaiders.Api.Application.Common.Interfaces.Repositories;
using CsRaiders.Api.Application.Common.Models;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public async Task<User?> FindByEmailAsync(Email email, bool track = false, bool includeDeleted = false, CancellationToken ct = default)
    {
        var query = track ? db.Users.AsQueryable() : db.Users.AsNoTracking();
        if (includeDeleted)
        {
            query = query.IgnoreQueryFilters();
        }

        return await query.FirstOrDefaultAsync(u => u.Email == email, ct);
    }

    public async Task<User?> FindByIdAsync(Guid id, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.Users.AsQueryable() : db.Users.AsNoTracking();
        return await query.FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User?> FindByIdWithPermissionsAsync(Guid id, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.Users.AsQueryable() : db.Users.AsNoTracking();
        return await query
            .Include(u => u.Roles)
            .Include(u => u.PermissionOverrides)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<bool> ExistsAsync(Email email, CancellationToken ct = default)
    {
        return await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email, ct);
    }

    public async Task<PagedResult<UserSummary>> ListAsync(UserFilter filter, CancellationToken ct = default)
    {
        var query = db.Users.AsNoTracking();

        if (filter.Status.HasValue)
        {
            query = query.Where(u => u.Status == filter.Status.Value);
        }

        if (filter.RoleId.HasValue)
        {
            query = query.Where(u => u.Roles.Any(r => r.RoleId == filter.RoleId.Value));
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.Name, $"%{term}%") ||
                EF.Functions.ILike(EF.Property<string>(u, "Email"), $"%{term}%"));
        }

        if (filter.CursorId.HasValue)
        {
            query = query.Where(u => u.Id > filter.CursorId.Value);
        }

        var limit = Math.Clamp(filter.Limit, 1, 100);
        var items = await query
            .OrderBy(u => u.Id)
            .Take(limit + 1)
            .Select(u => new UserSummary(
                u.Id,
                EF.Property<string>(u, "Email"),
                u.Name,
                u.Phone,
                u.WhatsApp,
                u.AcademicYear,
                u.Status,
                u.CreatedAt))
            .ToListAsync(ct);

        Guid? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(limit);
            nextCursor = items[^1].Id;
        }

        return new PagedResult<UserSummary>(items, nextCursor, items.Count);
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await db.Users.AddAsync(user, ct);
    }

    public void Update(User user)
    {
        db.Users.Update(user);
    }
}
