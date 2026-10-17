using Atlas.Domain.Spaces;
using Atlas.Domain.Users;

namespace Atlas.Domain.Tests.Spaces;

public class SpaceAccessPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeptA = Guid.NewGuid();
    private static readonly Guid DeptB = Guid.NewGuid();

    private static User Ordinary(string name = "john", Guid? dept = null, Guid? profile = null)
    {
        var user = User.Create(name, "hash", UserRole.User, false, T0);
        if (dept is { } d) user.AssignToDepartment(d, T0);
        if (profile is { } p) user.AssignProfile(p, T0);
        return user;
    }

    [Fact]
    public void Every_authenticated_user_reads_the_public_space_whatever_their_state()
    {
        var orphan = Ordinary();
        var deptOnly = Ordinary("deptOnly", DeptA);
        var profileOnly = Ordinary("profileOnly", null, Guid.NewGuid());
        var full = Ordinary("fullMember", DeptA, Guid.NewGuid());
        var admin = User.Create("root.admin", "hash", UserRole.Admin, false, T0);

        foreach (var user in new[] { orphan, deptOnly, profileOnly, full, admin })
            Assert.Equal(SpaceAccess.Read, SpaceAccessPolicy.Evaluate(user, DataSpace.Public));
    }

    [Fact]
    public void Orphan_has_no_private_and_no_department_access()
    {
        var orphan = Ordinary();

        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(orphan, DataSpace.PrivateOf(orphan.Id)));
        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(orphan, DataSpace.DepartmentOf(DeptA)));
    }

    [Fact]
    public void Member_with_profile_reaches_own_private_and_own_department_space_only()
    {
        var member = Ordinary("member", DeptA, Guid.NewGuid());

        Assert.Equal(SpaceAccess.Read, SpaceAccessPolicy.Evaluate(member, DataSpace.PrivateOf(member.Id)));
        Assert.Equal(SpaceAccess.Read, SpaceAccessPolicy.Evaluate(member, DataSpace.DepartmentOf(DeptA)));
        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(member, DataSpace.DepartmentOf(DeptB)));
    }

    [Fact]
    public void Sharing_a_department_does_not_expose_another_members_private_space()
    {
        var alice = Ordinary("alice", DeptA, Guid.NewGuid());
        var bob = Ordinary("bob", DeptA, Guid.NewGuid());

        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(alice, DataSpace.PrivateOf(bob.Id)));
    }

    [Fact]
    public void Department_without_profile_or_profile_without_department_gives_no_organizational_access()
    {
        var noProfile = Ordinary("noProfile", DeptA);
        var noDepartment = Ordinary("noDepartment", null, Guid.NewGuid());

        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(noProfile, DataSpace.DepartmentOf(DeptA)));
        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(noProfile, DataSpace.PrivateOf(noProfile.Id)));
        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(noDepartment, DataSpace.PrivateOf(noDepartment.Id)));
    }

    [Fact]
    public void Administrators_only_read_the_public_space()
    {
        var admin = User.Create("root.admin", "hash", UserRole.Admin, false, T0);

        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(admin, DataSpace.PrivateOf(admin.Id)));
        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(admin, DataSpace.DepartmentOf(DeptA)));
    }

    [Fact]
    public void Inactive_accounts_get_nothing()
    {
        var user = Ordinary("inactive", DeptA, Guid.NewGuid());
        user.Deactivate(T0);

        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(user, DataSpace.Public));
        Assert.Equal(SpaceAccess.None, SpaceAccessPolicy.Evaluate(user, DataSpace.DepartmentOf(DeptA)));
    }

    [Fact]
    public void Write_is_never_granted_in_this_phase()
    {
        var member = Ordinary("member", DeptA, Guid.NewGuid());
        var spaces = new[] { DataSpace.Public, DataSpace.PrivateOf(member.Id), DataSpace.DepartmentOf(DeptA) };

        foreach (var space in spaces)
            Assert.False(SpaceAccessPolicy.Evaluate(member, space).HasFlag(SpaceAccess.Write));
    }

    [Fact]
    public void Access_does_not_depend_on_having_a_department_head()
    {
        // The policy has no notion of a head at all: a headless department's members are unaffected.
        var member = Ordinary("member", DeptA, Guid.NewGuid());
        Assert.Equal(SpaceAccess.Read, SpaceAccessPolicy.Evaluate(member, DataSpace.DepartmentOf(DeptA)));
    }
}
