using Inmobiliaria.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inmobiliaria.Infrastructure.Persistence.Configurations;

/// <summary>
/// users-and-roles: the FK target every other table points at when it needs to name a person
/// (design Decision 8). <c>username</c> equals the PostgreSQL <c>rolname</c>, so it carries
/// both a lowercase CHECK — mirroring the guard <see cref="AppUser"/>'s constructor already
/// enforces in memory — and a unique index. No role column: role is read back from
/// <c>pg_has_role</c> at login and never mirrored here (spec "Application Role Read From
/// PostgreSQL, Never Mirrored").
/// </summary>
public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("app_users", t => t.HasCheckConstraint(
            "ck_app_users_username_lowercase",
            "username = lower(username)"));

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username)
            .HasColumnName("username")
            .IsRequired();

        builder.Property(u => u.DisplayName)
            .HasColumnName("display_name")
            .IsRequired();

        builder.Property(u => u.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(u => u.MustChangePassword)
            .HasColumnName("must_change_password")
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(u => u.Username)
            .IsUnique()
            .HasDatabaseName("ix_app_users_username");
    }
}
