using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.GetUser;
using Atlas.Application.Users.ListUsers;
using Atlas.Application.Users.ResetUserPassword;
using Atlas.Application.Users.SetUserStatus;
using Atlas.Application.Users.UpdateUser;
using Atlas.Domain.Users;
using MediatR;

namespace Atlas.Application.Tests.Users;

public class AdminOnlyTests
{
    [Fact]
    public async Task Every_admin_use_case_rejects_a_normal_user()
    {
        using var app = new AppFixture();
        var target = app.Users.Seed("target.user");
        app.Current.SignInAs(app.Users.Seed("regular.user"));

        var requests = new IBaseRequest[]
        {
            new ListUsersQuery(1, 10, null, null, null),
            new GetUserQuery(target.Id),
            new UpdateUserCommand(target.Id, "target.user", UserRole.Admin),
            new SetUserStatusCommand(target.Id, false),
            new ResetUserPasswordCommand(target.Id)
        };

        foreach (var request in requests)
        {
            var result = (Result)(await app.Get<ISender>().Send(request))!;
            AppFixture.AssertFailure(result, ErrorType.Forbidden, "forbidden");
        }

        Assert.True(target.IsActive);
        Assert.Equal(UserRole.User, target.Role);
    }

    [Fact]
    public async Task Admin_can_list_with_pagination_and_filters()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        for (var i = 1; i <= 5; i++) app.Users.Seed($"worker{i}");
        app.Users.Seed("sleeper", active: false);

        var page2 = await app.Ok(new ListUsersQuery(2, 2, "WORK", null, null));
        Assert.Equal(5, page2.TotalCount);
        Assert.Equal(3, page2.TotalPages);
        Assert.Equal(["worker3", "worker4"], page2.Items.Select(u => u.Username).ToArray());

        var inactive = await app.Ok(new ListUsersQuery(1, 10, null, null, false));
        Assert.Equal("sleeper", Assert.Single(inactive.Items).Username);

        var admins = await app.Ok(new ListUsersQuery(1, 10, null, UserRole.Admin, null));
        Assert.Equal("root.admin", Assert.Single(admins.Items).Username);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_paging_is_a_validation_failure(int page, int size)
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await app.Fail(new ListUsersQuery(page, size, null, null, null), ErrorType.Validation);
    }

    [Fact]
    public async Task Get_unknown_user_is_not_found()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await app.Fail(new GetUserQuery(Guid.NewGuid()), ErrorType.NotFound);
    }
}
