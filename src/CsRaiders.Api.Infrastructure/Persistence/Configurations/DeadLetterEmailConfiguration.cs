using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CsRaiders.Api.Infrastructure.Persistence.Configurations;

internal sealed class DeadLetterEmailConfiguration : IEntityTypeConfiguration<DeadLetterEmail>
{
    public void Configure(EntityTypeBuilder<DeadLetterEmail> b)
    {
        b.ToTable("dead_letter_emails");
        b.ConfigureBase();

        b.Property(x => x.Recipient).HasColumnType("citext").IsRequired();
        b.Property(x => x.Subject).HasMaxLength(500).IsRequired();
        b.Property(x => x.Purpose).HasMaxLength(50).IsRequired();
        b.Property(x => x.ErrorReason).HasColumnType("text").IsRequired();
        b.Property(x => x.AttemptCount).IsRequired();
        b.Property(x => x.CorrelationId).HasMaxLength(128);
        b.Property(x => x.FailedAt).IsRequired();

        b.HasIndex(x => x.FailedAt).IsDescending().HasDatabaseName("ix_dead_letter_emails_failed_at");
        b.HasIndex(x => x.Recipient).HasDatabaseName("ix_dead_letter_emails_recipient");
    }
}
