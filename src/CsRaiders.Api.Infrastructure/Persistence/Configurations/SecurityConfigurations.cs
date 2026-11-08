using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CsRaiders.Api.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(200);
        b.Property(x => x.Value).IsRequired();
        b.Property(x => x.UpdatedAt).IsRequired();
    }
}

internal sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> b)
    {
        b.ToTable("security_events");
        b.ConfigureBase();
        b.Property(x => x.EventName).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasColumnType("citext");
        b.Property(x => x.Details).HasColumnType("jsonb").IsRequired();
        b.HasOne<CsRaiders.Api.Domain.Entities.User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_security_events_created_at");
        b.HasIndex(x => x.UserId).HasDatabaseName("ix_security_events_user_id");
    }
}
