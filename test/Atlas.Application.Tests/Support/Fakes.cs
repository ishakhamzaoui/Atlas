using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Support;

public sealed class FakeUserRepository : IUserRepository, IUserWriteScope
{
    public List<User> Items { get; } = [];

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByUsernameAsync(string normalizedUsername, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.NormalizedUsername == normalizedUsername));

    public Task<bool> UsernameExistsAsync(string normalizedUsername, Guid? excludingUserId, CancellationToken ct) =>
        Task.FromResult(Items.Any(u => u.NormalizedUsername == normalizedUsername && u.Id != excludingUserId));

    public Task<bool> AnyAdminAsync(CancellationToken ct) =>
        Task.FromResult(Items.Any(u => u.Role == UserRole.Admin));

    public Task<int> CountActiveAdminsAsync(Guid excludingUserId, CancellationToken ct) =>
        Task.FromResult(Items.Count(u => u.Role == UserRole.Admin && u.IsActive && u.Id != excludingUserId));

    public Task<PagedResult<User>> ListAsync(UserListFilter f, CancellationToken ct)
    {
        var q = Items.AsEnumerable();
        if (f.Role is { } r) q = q.Where(u => u.Role == r);
        if (f.IsActive is { } a) q = q.Where(u => u.IsActive == a);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = UsernamePolicy.Normalize(f.Search);
            q = q.Where(u => u.NormalizedUsername.Contains(s));
        }

        var all = q.OrderBy(u => u.NormalizedUsername).ToList();
        var page = all.Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToList();
        return Task.FromResult(new PagedResult<User>(page, f.Page, f.PageSize, all.Count));
    }

    public Task AddAsync(User user, CancellationToken ct)
    {
        if (Items.Any(u => u.NormalizedUsername == user.NormalizedUsername))
            throw new ConflictException("username_taken", "That username is already in use.");
        Items.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => action(ct);

    public User Seed(string username, UserRole role = UserRole.User, bool active = true, bool mustChange = false,
        string password = "Current#Passw0rd1")
    {
        var user = User.Create(username, FakePasswordHasher.HashOf(password), role, mustChange, DateTimeOffset.UtcNow);
        if (!active) user.Deactivate(DateTimeOffset.UtcNow);
        Items.Add(user);
        return user;
    }
}

public sealed class FakePasswordHasher : IPasswordHasher
{
    public static string HashOf(string password) => "hash:" + password;
    public string Hash(string password) => HashOf(password);
    public bool Verify(string password, string passwordHash) => passwordHash == HashOf(password);
    public string DummyHash => "hash:__dummy__";
}

public sealed class FakePasswordGenerator : IPasswordGenerator
{
    public const string Value = "Generated#Temp-Pass9";
    public string Generate() => Value;
}

public sealed class FakeTokenService : ITokenService
{
    public AccessToken Create(User user) =>
        new($"token-for-{user.Username}-{(user.MustChangePassword ? "restricted" : "full")}", DateTimeOffset.UtcNow.AddMinutes(15));
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }
    public Guid? UserId { get; set; }
    public string? Username { get; set; }
    public bool IsAdmin { get; set; }

    public void SignInAs(User user)
    {
        IsAuthenticated = true;
        UserId = user.Id;
        Username = user.Username;
        IsAdmin = user.Role == UserRole.Admin;
    }

    public void SignOut()
    {
        IsAuthenticated = false;
        UserId = null;
        Username = null;
        IsAdmin = false;
    }
}
