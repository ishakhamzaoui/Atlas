using Atlas.Domain.Auth;
using Atlas.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id).HasName("pk_refresh_tokens");
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(t => t.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(t => t.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(t => t.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(t => t.SecurityStamp).HasColumnName("security_stamp").IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(t => t.AbsoluteExpiresAt).HasColumnName("absolute_expires_at").IsRequired();
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");
        builder.Property(t => t.ReplacedByTokenId).HasColumnName("replaced_by_token_id");

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires_at");

        // Deleting a user removes their sessions.
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_refresh_tokens_users_user_id");

        // Optimistic concurrency (xmin): two simultaneous rotations of the same token cannot both succeed.
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
