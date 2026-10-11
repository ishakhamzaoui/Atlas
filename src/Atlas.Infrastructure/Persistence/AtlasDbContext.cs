using Atlas.Domain.Auth;
using Atlas.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence;

public sealed class AtlasDbContext(DbContextOptions<AtlasDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtlasDbContext).Assembly);
}
