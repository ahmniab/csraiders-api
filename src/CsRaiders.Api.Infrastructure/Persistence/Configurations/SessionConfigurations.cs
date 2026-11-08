using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CsRaiders.Api.Infrastructure.Persistence.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> b)
    {
        b.ToTable("sessions", t => t.HasCheckConstraint("ck_sessions_revoke_reason",
            "revoke_reason IS NULL OR revoke_reason IN " +
            "('superseded','cap_exceeded','policy_reduced','reuse_detected','logout','password_reset','admin_revoked','account_disabled')"));
        b.ConfigureBase();

        b.Property(s => s.RefreshTokenHash).IsRequired();                   // bytea
        b.Property(s => s.DeviceFp).HasMaxLength(128).IsRequired();
        b.Property(s => s.UserAgent).HasMaxLength(512);                     // Ip maps to inet automatically

        b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        // FR-61: the database refuses a second live single-session row, even under a race.
        b.HasIndex(s => s.UserId).IsUnique()
            .HasFilter("revoked_at IS NULL AND enforce_single_session = true")
            .HasDatabaseName("uq_sessions_one_active");

        b.HasIndex(s => new { s.UserId, s.CreatedAt }).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_sessions_user_active");
        b.HasIndex(s => s.RefreshTokenHash).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_sessions_refresh_hash");
        b.HasIndex(s => s.PreviousRefreshTokenHash).HasFilter("previous_refresh_token_hash IS NOT NULL").HasDatabaseName("ix_sessions_prev_refresh_hash");
        b.HasIndex(s => s.ExpiresAt).HasDatabaseName("ix_sessions_expires");
    }
}

internal sealed class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> b)
    {
        b.ToTable("login_attempts");
        b.ConfigureBase();

        b.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.SetNull);
        // The composite per-(email, ip) uniqueness is created as a database-only
        // index in the hardening migration. Npgsql maps inet to IPAddress, which
        // cannot be used in EF's change-tracker key/index comparer.
        b.HasIndex(a => new { a.LastIp, a.FirstFailureAt }).HasDatabaseName("ix_login_attempts_ip_window");
        b.HasIndex(a => a.UpdatedAt).HasDatabaseName("ix_login_attempts_updated");   // nightly purge
    }
}

internal sealed class PendingRegistrationConfiguration : IEntityTypeConfiguration<PendingRegistration>
{
    public void Configure(EntityTypeBuilder<PendingRegistration> b)
    {
        b.ToTable("pending_registrations");
        b.ConfigureBase();

        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.PasswordHash).IsRequired();
        b.Property(p => p.OtpHash).IsRequired();
        b.Property(p => p.DeviceFp).HasMaxLength(128);

        b.HasIndex(p => p.Email).IsUnique().HasDatabaseName("ux_pending_email");
        b.HasIndex(p => p.ExpiresAt).HasDatabaseName("ix_pending_expires");        // nightly purge job
    }
}

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("password_reset_tokens");
        b.ConfigureBase();

        b.Property(t => t.TokenHash).IsRequired();
        b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_password_reset_tokens_hash");
        b.HasIndex(t => t.UserId).HasDatabaseName("ix_password_reset_tokens_user");
    }
}
