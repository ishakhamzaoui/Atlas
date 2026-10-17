using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Organization;
using Atlas.Domain.Users;
using Atlas.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Tests.Persistence;

public class OrganizationRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..10];

    private async Task<Department> NewDepartmentAsync(string? name = null)
    {
        var department = Department.Create(name ?? Unique("Dept "), null, Now);
        await using var db = postgres.CreateContext();
        await new DepartmentRepository(db).AddAsync(department, default);
        return department;
    }

    private async Task<Profile> NewProfileAsync(string? name = null)
    {
        var profile = Profile.Create(name ?? Unique("Profile "), null, Now);
        await using var db = postgres.CreateContext();
        await new ProfileRepository(db).AddAsync(profile, default);
        return profile;
    }

    private async Task<User> NewUserAsync()
    {
        var user = User.Create(Unique("u"), "hash", UserRole.User, false, Now);
        await using var db = postgres.CreateContext();
        await new UserRepository(db).AddAsync(user, default);
        return user;
    }

    private async Task UpdateUserAsync(Guid id, Action<User> change)
    {
        await using var db = postgres.CreateContext();
        var repo = new UserRepository(db);
        var user = (await repo.GetByIdAsync(id, default))!;
        change(user);
        await repo.SaveChangesAsync(default);
    }

    // ---------- optional assignments are persisted as NULL ----------

    [Fact]
    public async Task User_without_department_or_profile_round_trips_as_an_orphan()
    {
        var user = await NewUserAsync();

        await using var db = postgres.CreateContext();
        var loaded = await new UserRepository(db).GetByIdAsync(user.Id, default);

        Assert.Null(loaded!.DepartmentId);
        Assert.Null(loaded.ProfileId);
        Assert.True(loaded.IsOrphan);
    }

    [Fact]
    public async Task Assignments_persist_and_can_be_cleared_without_deleting_anything()
    {
        var user = await NewUserAsync();
        var department = await NewDepartmentAsync();
        var profile = await NewProfileAsync();

        await UpdateUserAsync(user.Id, u => { u.AssignToDepartment(department.Id, Now); u.AssignProfile(profile.Id, Now); });
        await using (var db = postgres.CreateContext())
        {
            var loaded = (await new UserRepository(db).GetByIdAsync(user.Id, default))!;
            Assert.Equal(department.Id, loaded.DepartmentId);
            Assert.Equal(profile.Id, loaded.ProfileId);
        }

        await UpdateUserAsync(user.Id, u => { u.RemoveProfile(Now); u.RemoveFromDepartment(Now); });
        await using var read = postgres.CreateContext();
        var after = (await new UserRepository(read).GetByIdAsync(user.Id, default))!;
        Assert.True(after.IsOrphan);
        Assert.NotNull(await new DepartmentRepository(read).GetByIdAsync(department.Id, default)); // retained
        Assert.NotNull(await new ProfileRepository(read).GetByIdAsync(profile.Id, default));       // retained
    }

    // ---------- departments ----------

    [Fact]
    public async Task Department_without_head_and_without_members_is_valid_and_summarized()
    {
        var department = await NewDepartmentAsync();

        await using var db = postgres.CreateContext();
        var summary = (await new DepartmentRepository(db).GetSummaryAsync(department.Id, default))!;

        Assert.Null(summary.Department.HeadUserId);
        Assert.Null(summary.HeadUsername);
        Assert.Equal(0, summary.MemberCount);
    }

    [Fact]
    public async Task Department_names_are_unique_case_insensitively_in_the_database()
    {
        var name = Unique("Same ");
        await NewDepartmentAsync(name);

        await using var db = postgres.CreateContext();
        var ex = await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            new DepartmentRepository(db).AddAsync(Department.Create(name.ToLowerInvariant(), null, Now), default));
        Assert.Equal(PersistenceConflictKind.DuplicateDepartmentName, ex.Kind);
    }

    [Fact]
    public async Task Many_departments_may_be_headless_at_the_same_time()
    {
        var a = await NewDepartmentAsync();
        var b = await NewDepartmentAsync();

        await using var db = postgres.CreateContext();
        var repo = new DepartmentRepository(db);
        Assert.Null((await repo.GetByIdAsync(a.Id, default))!.HeadUserId);
        Assert.Null((await repo.GetByIdAsync(b.Id, default))!.HeadUserId); // NULL heads never collide on the unique index
    }

    [Fact]
    public async Task Head_summary_member_count_and_member_listing_reflect_membership()
    {
        var department = await NewDepartmentAsync();
        var head = await NewUserAsync();
        var member = await NewUserAsync();
        await UpdateUserAsync(head.Id, u => u.AssignToDepartment(department.Id, Now));
        await UpdateUserAsync(member.Id, u => u.AssignToDepartment(department.Id, Now));

        await using (var db = postgres.CreateContext())
        {
            var repo = new DepartmentRepository(db);
            var tracked = (await repo.GetByIdAsync(department.Id, default))!;
            tracked.AppointHead(head.Id, Now);
            await repo.SaveChangesAsync(default);
        }

        await using var read = postgres.CreateContext();
        var summary = (await new DepartmentRepository(read).GetSummaryAsync(department.Id, default))!;
        Assert.Equal(head.Id, summary.Department.HeadUserId);
        Assert.Equal(head.Username, summary.HeadUsername);
        Assert.Equal(2, summary.MemberCount);

        var members = await new UserRepository(read).ListAsync(new UserListFilter(1, 10, null, null, null, DepartmentId: department.Id), default);
        Assert.Equal(2, members.TotalCount);
    }

    [Fact]
    public async Task A_user_can_lead_at_most_one_department()
    {
        var a = await NewDepartmentAsync();
        var b = await NewDepartmentAsync();
        var user = await NewUserAsync();

        await using (var db = postgres.CreateContext())
        {
            var repo = new DepartmentRepository(db);
            (await repo.GetByIdAsync(a.Id, default))!.AppointHead(user.Id, Now);
            await repo.SaveChangesAsync(default);
        }

        await using var db2 = postgres.CreateContext();
        var repo2 = new DepartmentRepository(db2);
        (await repo2.GetByIdAsync(b.Id, default))!.AppointHead(user.Id, Now);
        await Assert.ThrowsAsync<PersistenceConflictException>(() => repo2.SaveChangesAsync(default));
    }

    [Fact]
    public async Task Pointing_a_user_at_a_missing_department_or_profile_is_rejected_by_foreign_keys()
    {
        var user = await NewUserAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => UpdateUserAsync(user.Id, u => u.AssignToDepartment(Guid.NewGuid(), Now)));
        await Assert.ThrowsAsync<DbUpdateException>(() => UpdateUserAsync(user.Id, u => u.AssignProfile(Guid.NewGuid(), Now)));
    }

    [Fact]
    public async Task Deleting_a_department_or_profile_row_cannot_cascade_into_user_accounts()
    {
        var department = await NewDepartmentAsync();
        var profile = await NewProfileAsync();
        var user = await NewUserAsync();
        await UpdateUserAsync(user.Id, u => { u.AssignToDepartment(department.Id, Now); u.AssignProfile(profile.Id, Now); });

        await using var db = postgres.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Departments.Where(d => d.Id == department.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAnyAsync<Exception>(() => db.Profiles.Where(p => p.Id == profile.Id).ExecuteDeleteAsync());

        await using var read = postgres.CreateContext();
        Assert.NotNull(await new UserRepository(read).GetByIdAsync(user.Id, default));
    }

    // ---------- profiles ----------

    [Fact]
    public async Task Profile_names_are_unique_case_insensitively_in_the_database()
    {
        var name = Unique("Same ");
        await NewProfileAsync(name);

        await using var db = postgres.CreateContext();
        var ex = await Assert.ThrowsAsync<PersistenceConflictException>(() =>
            new ProfileRepository(db).AddAsync(Profile.Create(name.ToUpperInvariant(), null, Now), default));
        Assert.Equal(PersistenceConflictKind.DuplicateProfileName, ex.Kind);
    }

    [Fact]
    public async Task Profile_counts_and_assigned_filter_work_and_unassigned_profiles_are_retained()
    {
        var marker = Unique("flt");
        var held = await NewProfileAsync($"{marker} held");
        var spare = await NewProfileAsync($"{marker} spare");
        var user = await NewUserAsync();
        await UpdateUserAsync(user.Id, u => u.AssignProfile(held.Id, Now));

        await using (var db = postgres.CreateContext())
        {
            var repo = new ProfileRepository(db);
            Assert.Equal(1, (await repo.GetSummaryAsync(held.Id, default))!.AssignedUserCount);
            Assert.Equal(0, (await repo.GetSummaryAsync(spare.Id, default))!.AssignedUserCount);

            var all = await repo.ListAsync(new ProfileListFilter(1, 10, marker, null), default);
            var assigned = await repo.ListAsync(new ProfileListFilter(1, 10, marker, true), default);
            var unassigned = await repo.ListAsync(new ProfileListFilter(1, 10, marker, false), default);
            Assert.Equal(2, all.TotalCount);
            Assert.Equal(held.Id, Assert.Single(assigned.Items).Profile.Id);
            Assert.Equal(spare.Id, Assert.Single(unassigned.Items).Profile.Id);
        }

        // once the user lets go of the profile it is simply unassigned, not gone
        await UpdateUserAsync(user.Id, u => u.RemoveProfile(Now));
        await using var after = postgres.CreateContext();
        var summary = (await new ProfileRepository(after).GetSummaryAsync(held.Id, default))!;
        Assert.Equal(0, summary.AssignedUserCount);
    }

    // ---------- user listing filters ----------

    [Fact]
    public async Task User_listing_filters_by_department_profile_and_orphan_state()
    {
        var department = await NewDepartmentAsync();
        var profile = await NewProfileAsync();
        var orphan = await NewUserAsync();
        var member = await NewUserAsync();
        var profileOnly = await NewUserAsync();
        await UpdateUserAsync(member.Id, u => u.AssignToDepartment(department.Id, Now));
        await UpdateUserAsync(profileOnly.Id, u => u.AssignProfile(profile.Id, Now));

        await using var db = postgres.CreateContext();
        var repo = new UserRepository(db);

        var byDepartment = await repo.ListAsync(new UserListFilter(1, 50, null, null, null, DepartmentId: department.Id), default);
        Assert.Equal(member.Id, Assert.Single(byDepartment.Items).Id);

        var byProfile = await repo.ListAsync(new UserListFilter(1, 50, null, null, null, ProfileId: profile.Id), default);
        Assert.Equal(profileOnly.Id, Assert.Single(byProfile.Items).Id);

        var orphans = await repo.ListAsync(new UserListFilter(1, 500, null, null, null, Orphan: true), default);
        Assert.Contains(orphans.Items, u => u.Id == orphan.Id);
        Assert.DoesNotContain(orphans.Items, u => u.Id == member.Id || u.Id == profileOnly.Id);

        var organized = await repo.ListAsync(new UserListFilter(1, 500, null, null, null, Orphan: false), default);
        Assert.DoesNotContain(organized.Items, u => u.Id == orphan.Id);
        Assert.Contains(organized.Items, u => u.Id == member.Id);
    }
}
