using Atlas.Domain.Organization;
using Atlas.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atlas.Infrastructure.Persistence.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");

        builder.HasKey(d => d.Id).HasName("pk_departments");
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(d => d.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.NormalizedName).HasColumnName("normalized_name").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Description).HasColumnName("description").HasMaxLength(500);

        // OPTIONAL head: a department with no head is valid. (That the head is a member of the department is enforced
        // by the use cases inside the exclusive organization scope; the database only guarantees the user exists.)
        builder.Property(d => d.HeadUserId).HasColumnName("head_user_id");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(d => d.NormalizedName).IsUnique().HasDatabaseName("ux_departments_normalized_name");

        // A user can lead at most one department (NULLs are not considered equal, so many headless departments are fine).
        builder.HasIndex(d => d.HeadUserId).IsUnique().HasDatabaseName("ux_departments_head_user_id");

        builder.HasOne<User>().WithMany().HasForeignKey(d => d.HeadUserId)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_departments_users_head_user_id");
    }
}
