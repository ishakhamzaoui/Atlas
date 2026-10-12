using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Users;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atlas.Infrastructure.Tests.Persistence;

public class UserRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static string Unique(string prefix = "u") => prefix + Guid.NewGuid().ToString("N")[..12];

    private static User NewUser(string? name = null, UserRole role = UserRole.User) =>
        User.Create(name ?? Unique(), "hash", role, true, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Add_and_read_back_round_trips_all_fields()
    {
        var user = NewUser();
        await using (var db = postgres.CreateContext())
            await new UserRepository(db).AddAsync(user, default);

        await using var read = postgres.CreateContext();
        var loaded = await new UserRepository(read).GetByUsernameAsync(user.NormalizedUsername, default);

        Assert.NotNull(loaded);
        Assert.Equal(user.Id, loaded!.Id);
        Assert.Equal(user.Username, loaded.Username);
        Assert.Equal(UserRole.User, loaded.Role);
        Assert.True(loaded.IsActive);
        Assert.True(loaded.MustChangePassword);
        Assert.Equal(user.SecurityStamp, loaded.SecurityStamp);
    }

    [Fact]
    public async Task Username_uniqueness_is_case_insensitive_and_enforced_by_the_database()
    {
        var name = Unique("Case");
        await using (var db = postgres.CreateContext())
            await new UserRepository(db).AddAsync(NewUser(name), default);

        await using var db2 = postgres.CreateContext();
        var ex = await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            new UserRepository(db2).AddAsync(NewUser(name.ToLowerInvariant()), default));

        Assert.Equal(PersistenceConflictKind.DuplicateUsername, ex.Kind);
    }

    [Fact]
    public async Task Concurrent_inserts_of_the_same_username_yield_exactly_one_winner()
    {
        var name = Unique("Race");

        async Task<bool> TryInsert()
        {
            await using var db = postgres.CreateContext();
            try { await new UserRepository(db).AddAsync(NewUser(name), default); return true; }
            catch (PersistenceConflictException) { return false; }
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => TryInsert()));
        Assert.Single(results, r => r);
    }

    [Fact]
    public async Task Database_rejects_rows_that_bypass_domain_rules()
    {
        await using var db = postgres.CreateContext();
        var id = Guid.NewGuid();

        // invalid role
        var badRole = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO users (id, username, normalized_username, password_hash, role, is_active, must_change_password, security_stamp, created_at, updated_at)
             VALUES ({id}, 'okname', 'OKNAME', 'h', 'Root', true, false, {Guid.NewGuid()}, now(), now())
             """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badRole.SqlState);

        // normalized value not matching upper(username)
        var badNormalized = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO users (id, username, normalized_username, password_hash, role, is_active, must_change_password, security_stamp, created_at, updated_at)
             VALUES ({Guid.NewGuid()}, 'MixedCase', 'mixedcase', 'h', 'User', true, false, {Guid.NewGuid()}, now(), now())
             """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, badNormalized.SqlState);
    }

    [Fact]
    public async Task Admin_counting_and_listing_filters_work()
    {
        var marker = Unique("flt");
        await using (var db = postgres.CreateContext())
        {
            var repo = new UserRepository(db);
            var activeAdmin = NewUser(marker + "adm1", UserRole.Admin);
            var inactiveAdmin = NewUser(marker + "adm2", UserRole.Admin);
            inactiveAdmin.Deactivate(DateTimeOffset.UtcNow);
            await repo.AddAsync(activeAdmin, default);
            await repo.AddAsync(inactiveAdmin, default);
            await repo.AddAsync(NewUser(marker + "usr1"), default);
        }

        await using var read = postgres.CreateContext();
        var repository = new UserRepository(read);

        Assert.True(await repository.AnyAdminAsync(default));

        var all = await repository.ListAsync(new UserListFilter(1, 10, marker, null, null), default);
        Assert.Equal(3, all.TotalCount);

        var admins = await repository.ListAsync(new UserListFilter(1, 10, marker, UserRole.Admin, true), default);
        Assert.Equal(marker + "adm1", Assert.Single(admins.Items).Username);

        var paged = await repository.ListAsync(new UserListFilter(2, 2, marker, null, null), default);
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);

        // wildcard characters are treated literally
        var wildcard = await repository.ListAsync(new UserListFilter(1, 10, "%", null, null), default);
        Assert.Equal(0, wildcard.TotalCount);
    }

    [Fact]
    public async Task Concurrent_modification_is_reported_as_conflict_instead_of_lost_update()
    {
        var user = NewUser();
        await using (var db = postgres.CreateContext())
            await new UserRepository(db).AddAsync(user, default);

        await using var first = postgres.CreateContext();
        await using var second = postgres.CreateContext();
        var repo1 = new UserRepository(first);
        var repo2 = new UserRepository(second);

        var u1 = await repo1.GetByIdAsync(user.Id, default);
        var u2 = await repo2.GetByIdAsync(user.Id, default);

        u1!.ChangePassword("hash-A", DateTimeOffset.UtcNow);
        await repo1.SaveChangesAsync(default);

        u2!.ChangePassword("hash-B", DateTimeOffset.UtcNow);
        var ex = await Assert.ThrowsAsync<PersistenceConflictException>(() => repo2.SaveChangesAsync(default));
        Assert.Equal(PersistenceConflictKind.ConcurrentUpdate, ex.Kind);
    }

    [Fact]
    public async Task Session_validator_tracks_stamp_and_active_flag()
    {
        var user = NewUser();
        await using (var db = postgres.CreateContext())
            await new UserRepository(db).AddAsync(user, default);

        await using var read = postgres.CreateContext();
        var validator = new Atlas.Infrastructure.Security.UserSessionValidator(read);

        Assert.True(await validator.IsSessionValidAsync(user.Id, user.SecurityStamp, default));
        Assert.False(await validator.IsSessionValidAsync(user.Id, Guid.NewGuid(), default));
        Assert.False(await validator.IsSessionValidAsync(Guid.NewGuid(), user.SecurityStamp, default));

        await using (var write = postgres.CreateContext())
        {
            var repo = new UserRepository(write);
            var tracked = await repo.GetByIdAsync(user.Id, default);
            tracked!.Deactivate(DateTimeOffset.UtcNow);
            await repo.SaveChangesAsync(default);
        }

        await using var read2 = postgres.CreateContext();
        Assert.False(await new Atlas.Infrastructure.Security.UserSessionValidator(read2)
            .IsSessionValidAsync(user.Id, user.SecurityStamp, default));
    }

    [Fact]
    public async Task Exclusive_write_scope_serialises_concurrent_work()
    {
        var concurrent = 0;
        var maxConcurrent = 0;

        async Task Work()
        {
            await using var db = postgres.CreateContext();
            await new UserWriteScope(db).RunExclusiveAsync<int>(async _ =>
            {
                var now = Interlocked.Increment(ref concurrent);
                InterlockedMax(ref maxConcurrent, now);
                await Task.Delay(100);
                Interlocked.Decrement(ref concurrent);
                return 0;
            }, default);
        }

        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Work()));
        Assert.Equal(1, maxConcurrent);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)))
            if (Interlocked.CompareExchange(ref target, value, current) == current) return;
    }
}
