using CsRaiders.Api.Domain.Constants;
using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence;

public sealed class IdentitySeeder(AppDbContext db)
{
    /// <param name="allowSessionMultiForStudents">
    /// Wire this to app_settings 'session.multi_default_for_new_students' in WS2. Until then it is false.
    /// </param>
    public async Task SeedAsync(bool allowSessionMultiForStudents, CancellationToken ct = default)
    {
        var existing = await db.Roles.Select(r => r.Slug).ToListAsync(ct);

        foreach (var preset in RolePresets.All.Where(p => !existing.Contains(p.Slug)))
        {
            var permissions = preset.Scope == RoleScope.Student && !allowSessionMultiForStudents
                ? preset.Permissions.Where(p => p != PermissionCatalog.SessionMulti)   // student safety belt
                : preset.Permissions;

            db.Roles.Add(Role.Create(preset.Name, preset.Slug, preset.Scope, permissions, isSystem: true));
        }

        await db.SaveChangesAsync(ct);
    }
}
