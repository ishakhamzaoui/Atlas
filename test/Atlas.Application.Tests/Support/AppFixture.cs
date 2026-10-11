using Atlas.Application.Common.Abstractions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Tests.Support;

/// <summary>Runs real MediatR pipelines (authorization + validation behaviors) against in-memory fakes.</summary>
public sealed class AppFixture : IDisposable
{
    private readonly ServiceProvider _provider;

    public FakeUserRepository Users { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeRefreshTokenRepository RefreshTokens { get; } = new();
    public FakeRefreshTokenProvider RefreshProvider { get; } = new();
    public MutableClock Clock { get; } = new(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
    public CapturingLoggerProvider Logs { get; } = new();

    public AppFixture()
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddApplication();
        services.AddSingleton<IUserRepository>(Users);
        services.AddSingleton<IUserWriteScope>(Users);
        services.AddSingleton<ICurrentUser>(Current);
        services.AddSingleton<IPasswordHasher, FakePasswordHasher>();
        services.AddSingleton<IPasswordGenerator, FakePasswordGenerator>();
        services.AddSingleton<ITokenService, FakeTokenService>();
        services.AddSingleton<IRefreshTokenRepository>(RefreshTokens);
        services.AddSingleton<IRefreshTokenProvider>(RefreshProvider);
        services.AddSingleton<TimeProvider>(Clock);
        _provider = services.BuildServiceProvider();
    }

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request) =>
        _provider.GetRequiredService<ISender>().Send(request);

    public Task Send(IRequest request) =>
        _provider.GetRequiredService<ISender>().Send(request);

    /// <summary>Seeds an active, fully-set-up admin and signs in as them.</summary>
    public Atlas.Domain.Users.User SignInAsAdmin(string name = "root.admin")
    {
        var admin = Users.Seed(name, Atlas.Domain.Users.UserRole.Admin);
        Current.SignInAs(admin);
        return admin;
    }

    public void Dispose() => _provider.Dispose();
}
