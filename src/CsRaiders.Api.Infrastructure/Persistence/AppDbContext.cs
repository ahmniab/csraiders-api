using CsRaiders.Api.Domain.Entities;
using CsRaiders.Api.Domain.Enums;
using CsRaiders.Api.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CsRaiders.Api.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");                       // emitted as CREATE EXTENSION in the migration
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);   // spec 16.4: one assembly scan
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder cb)
    {
        cb.Properties<Email>().HaveConversion<EmailConverter>().HaveColumnType("citext");

        cb.Properties<UserStatus>().HaveConversion<SnakeCaseEnumConverter<UserStatus>>();
        cb.Properties<PermissionEffect>().HaveConversion<SnakeCaseEnumConverter<PermissionEffect>>();
        cb.Properties<RoleScope>().HaveConversion<SnakeCaseEnumConverter<RoleScope>>();
        cb.Properties<SessionRevokeReason>().HaveConversion<SnakeCaseEnumConverter<SessionRevokeReason>>();
    }
}
