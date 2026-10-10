using Atlas.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            t.HasCheckConstraint("ck_users_role", "role IN ('User', 'Admin')");
            t.HasCheckConstraint("ck_users_username_format", "username ~ '^[A-Za-z0-9][A-Za-z0-9._-]{2,31}$'");
            t.HasCheckConstraint("ck_users_normalized_username", "normalized_username = upper(username)");
        });

        builder.HasKey(u => u.Id).HasName("pk_users");
        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(u => u.Username).HasColumnName("username").HasMaxLength(32).IsRequired();
        builder.Property(u => u.NormalizedUsername).HasColumnName("normalized_username").HasMaxLength(32).IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash").HasMaxLength(100).IsRequired();

        builder.Property(u => u.Role).HasColumnName("role").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(u => u.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(u => u.MustChangePassword).HasColumnName("must_change_password").IsRequired();
        builder.Property(u => u.SecurityStamp).HasColumnName("security_stamp").IsRequired();

        builder.Property(u => u.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(u => u.PasswordChangedAt).HasColumnName("password_changed_at");

        // Case-insensitive uniqueness is enforced by the database, not just by application pre-checks.
        builder.HasIndex(u => u.NormalizedUsername).IsUnique().HasDatabaseName("ux_users_normalized_username");
        builder.HasIndex(u => new { u.Role, u.IsActive }).HasDatabaseName("ix_users_role_is_active");

        // Optimistic concurrency via PostgreSQL's xmin system column (no extra column is created).
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
