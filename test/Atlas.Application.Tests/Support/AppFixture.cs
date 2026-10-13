using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
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
        // MediatR 13 logs a one-off license nag (Warning) on first use; it is a third-party notice, not application
        // logging, so it must not pollute the warnings the tests assert on.
        services.AddLogging(b => b
            .SetMinimumLevel(LogLevel.Trace)
            .AddFilter("LuckyPennySoftware.MediatR", LogLevel.None)
            .AddProvider(Logs));
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

    /// <summary>Sends a request that must succeed and returns its value.</summary>
    public async Task<T> Ok<T>(IRequest<Result<T>> request)
    {
        var result = await Send(request);
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error?.Code}: {result.Error?.Message}");
        return result.Value;
    }

    /// <summary>Sends a request that must fail with the given error type (and optional code); returns the error.</summary>
    public async Task<Error> Fail<T>(IRequest<Result<T>> request, ErrorType type, string? code = null)
    {
        var result = await Send(request);
        return AssertFailure(result, type, code);
    }

    public async Task OkVoid(IRequest<Result> request)
    {
        var result = await Send(request);
        Assert.True(result.IsSuccess, $"Expected success but got {result.Error?.Code}");
    }

    public static Error AssertFailure(Result result, ErrorType type, string? code)
    {
        Assert.True(result.IsFailure, "Expected a failure but the request succeeded.");
        Assert.Equal(type, result.Error!.Type);
        if (code is not null) Assert.Equal(code, result.Error.Code);
        return result.Error;
    }

    /// <summary>Seeds an active, fully-set-up admin and signs in as them.</summary>
    public Atlas.Domain.Users.User SignInAsAdmin(string name = "root.admin")
    {
        var admin = Users.Seed(name, Atlas.Domain.Users.UserRole.Admin);
        Current.SignInAs(admin);
        return admin;
    }

    public void Dispose() => _provider.Dispose();
}
