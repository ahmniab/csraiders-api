using CsRaiders.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CsRaiders.Api.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", t => t.HasCheckConstraint("ck_users_status", "status IN ('active','suspended','deleted')"));
        b.ConfigureBase();

        b.Property(u => u.Name).HasMaxLength(200).IsRequired();
        b.Property(u => u.Phone).HasMaxLength(32);
        b.Property(u => u.WhatsApp).HasMaxLength(32);
        b.Property(u => u.AcademicYear).HasMaxLength(32);
        b.Property(u => u.PasswordHash).IsRequired();
        b.Property(u => u.PepperVersion).HasDefaultValue((short)1).IsRequired();

        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("ux_users_email");   // citext => case-insensitive
        b.HasIndex(u => u.Status).HasDatabaseName("ix_users_status");
        b.HasQueryFilter(u => u.DeletedAt == null);                              // spec 16.4

        b.HasMany(u => u.Roles).WithOne().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(u => u.PermissionOverrides).WithOne().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(u => u.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(u => u.PermissionOverrides).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", t => t.HasCheckConstraint("ck_roles_scope", "scope IN ('student','staff')"));
        b.ConfigureBase();

        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Slug).HasMaxLength(100).IsRequired();

        // permissions text[] backed by the private list field
        b.Ignore(r => r.Permissions);
        b.Property<List<string>>("_permissions").HasColumnName("permissions").HasColumnType("text[]").IsRequired();

        b.HasIndex(r => r.Slug).IsUnique().HasDatabaseName("ux_roles_slug");
        b.HasIndex("_permissions").HasMethod("gin").HasDatabaseName("ix_roles_permissions");
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles");
        b.HasKey(x => new { x.UserId, x.RoleId });
        b.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.RoleId).HasDatabaseName("ix_user_roles_role");
    }
}

internal sealed class UserPermissionConfiguration : IEntityTypeConfiguration<UserPermission>
{
    public void Configure(EntityTypeBuilder<UserPermission> b)
    {
        b.ToTable("user_permissions", t => t.HasCheckConstraint("ck_user_permissions_effect", "effect IN ('grant','deny')"));
        b.HasKey(x => new { x.UserId, x.Permission });
        b.Property(x => x.Permission).HasMaxLength(64);
    }
}
