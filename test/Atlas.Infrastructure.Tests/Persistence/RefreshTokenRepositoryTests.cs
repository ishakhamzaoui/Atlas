using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Auth;
using Atlas.Domain.Users;
using Atlas.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Tests.Persistence;

public class RefreshTokenRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private async Task<User> NewUserAsync()
    {
        var user = User.Create("u" + Guid.NewGuid().ToString("N")[..12], "hash", UserRole.User, false, Now);
        await using var db = postgres.CreateContext();
        await new UserRepository(db).AddAsync(user, default);
        return user;
    }

    private static RefreshToken NewToken(User user, Guid? family = null, string? hash = null) =>
        RefreshToken.Issue(user.Id, hash ?? Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            family ?? Guid.NewGuid(), user.SecurityStamp, Now, TimeSpan.FromDays(7), Now.AddDays(30));

    private async Task SaveAsync(params RefreshToken[] tokens)
    {
        await using var db = postgres.CreateContext();
        var repo = new RefreshTokenRepository(db);
        foreach (var t in tokens) repo.Add(t);
        await repo.SaveChangesAsync(default);
    }

    [Fact]
    public async Task Token_round_trips_by_hash()
    {
        var user = await NewUserAsync();
        var token = NewToken(user);
        await SaveAsync(token);

        await using var db = postgres.CreateContext();
        var loaded = await new RefreshTokenRepository(db).GetByHashAsync(token.TokenHash, default);

        Assert.NotNull(loaded);
        Assert.Equal(token.Id, loaded!.Id);
        Assert.Equal(user.Id, loaded.UserId);
        Assert.Equal(token.FamilyId, loaded.FamilyId);
        Assert.Equal(user.SecurityStamp, loaded.SecurityStamp);
        Assert.Null(await new RefreshTokenRepository(db).GetByHashAsync("missing", default));
    }

    [Fact]
    public async Task Token_hash_is_unique()
    {
        var user = await NewUserAsync();
        var first = NewToken(user, hash: new string('a', 64));
        await SaveAsync(first);

        await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(NewToken(user, hash: new string('a', 64))));
    }

    [Fact]
    public async Task Revoking_a_family_only_touches_that_family()
    {
        var user = await NewUserAsync();
        var family = Guid.NewGuid();
        var a = NewToken(user, family);
        var b = NewToken(user, family);
        var other = NewToken(user);
        await SaveAsync(a, b, other);

        await using (var db = postgres.CreateContext())
            await new RefreshTokenRepository(db).RevokeFamilyAsync(family, Now, default);

        await using var read = postgres.CreateContext();
        var repo = new RefreshTokenRepository(read);
        Assert.True((await repo.GetByHashAsync(a.TokenHash, default))!.IsRevoked);
        Assert.True((await repo.GetByHashAsync(b.TokenHash, default))!.IsRevoked);
        Assert.False((await repo.GetByHashAsync(other.TokenHash, default))!.IsRevoked);
    }

    [Fact]
    public async Task Revoking_all_for_a_user_leaves_other_users_alone()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        var a1 = NewToken(alice);
        var a2 = NewToken(alice);
        var b1 = NewToken(bob);
        await SaveAsync(a1, a2, b1);

        await using (var db = postgres.CreateContext())
            await new RefreshTokenRepository(db).RevokeAllForUserAsync(alice.Id, Now, default);

        await using var read = postgres.CreateContext();
        var repo = new RefreshTokenRepository(read);
        Assert.True((await repo.GetByHashAsync(a1.TokenHash, default))!.IsRevoked);
        Assert.True((await repo.GetByHashAsync(a2.TokenHash, default))!.IsRevoked);
        Assert.False((await repo.GetByHashAsync(b1.TokenHash, default))!.IsRevoked);
    }

    [Fact]
    public async Task Simultaneous_rotation_of_the_same_token_conflicts()
    {
        var user = await NewUserAsync();
        var token = NewToken(user);
        await SaveAsync(token);

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var repo1 = new RefreshTokenRepository(first);
        var repo2 = new RefreshTokenRepository(second);

        var t1 = await repo1.GetByHashAsync(token.TokenHash, default);
        var t2 = await repo2.GetByHashAsync(token.TokenHash, default);

        t1!.Revoke(Now, Guid.NewGuid());
        await repo1.SaveChangesAsync(default);

        t2!.Revoke(Now, Guid.NewGuid());
        var ex = await Assert.ThrowsAsync<ConflictException>(() => repo2.SaveChangesAsync(default));
        Assert.Equal("concurrent_update", ex.Code);
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_refresh_tokens()
    {
        var user = await NewUserAsync();
        var token = NewToken(user);
        await SaveAsync(token);

        await using (var db = postgres.CreateContext())
            await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();

        await using var read = postgres.CreateContext();
        Assert.Null(await new RefreshTokenRepository(read).GetByHashAsync(token.TokenHash, default));
    }
}
