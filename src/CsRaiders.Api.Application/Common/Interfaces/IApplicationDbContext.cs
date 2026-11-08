using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<UserPermission> UserPermissions { get; }
    DbSet<Session> Sessions { get; }
    DbSet<LoginAttempt> LoginAttempts { get; }
    DbSet<PendingRegistration> PendingRegistrations { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
