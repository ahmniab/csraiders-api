using CsRaiders.Api.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CsRaiders.Api.Infrastructure.Persistence.Configurations;

internal static class EntityBuilderExtensions
{
    public static void ConfigureBase<T>(this EntityTypeBuilder<T> b) where T : Entity
    {
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();     // ids are generated in the domain
        b.Property(e => e.Version).IsRowVersion().HasColumnName("xmin").HasColumnType("xid");
    }
}
