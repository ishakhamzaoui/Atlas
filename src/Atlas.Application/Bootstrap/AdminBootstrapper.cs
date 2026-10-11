using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Bootstrap;

public enum BootstrapStatus
{
    Created,
    AlreadyInitialized,
    MissingPassword,
    WeakPassword,
    UsernameTaken
}

public sealed record BootstrapResult(BootstrapStatus Status, IReadOnlyList<string> Errors)
{
    public static BootstrapResult Of(BootstrapStatus status) => new(status, []);
}

/// <summary>
/// One-time creation of the initial <c>admin</c> account from an operator-supplied temporary password.
/// Idempotent and takeover-proof: it does nothing once any administrator exists, never touches existing
/// accounts, and is safe against concurrent runs because of the unique username index.
/// </summary>
public sealed class AdminBootstrapper(
    IUserRepository users,
    IPasswordHasher hasher,
    TimeProvider clock,
    ILogger<AdminBootstrapper> logger)
{
    public const string AdminUsername = "admin";

    public async Task<BootstrapResult> RunAsync(string? temporaryPassword, CancellationToken cancellationToken)
    {
        // Checked first: once initialised, the secret is not even looked at.
        if (await users.AnyAdminAsync(cancellationToken))
        {
            logger.LogInformation("Bootstrap skipped: an administrator already exists");
            return BootstrapResult.Of(BootstrapStatus.AlreadyInitialized);
        }

        if (string.IsNullOrWhiteSpace(temporaryPassword))
        {
            logger.LogError("Bootstrap aborted: no temporary password configured; no account was created");
            return BootstrapResult.Of(BootstrapStatus.MissingPassword);
        }

        var errors = PasswordPolicy.Validate(temporaryPassword, AdminUsername);
        if (errors.Count > 0)
        {
            logger.LogError("Bootstrap aborted: the temporary password violates the password policy ({ErrorCount} problems)", errors.Count);
            return new BootstrapResult(BootstrapStatus.WeakPassword, errors);
        }

        if (await users.UsernameExistsAsync(UsernamePolicy.Normalize(AdminUsername), null, cancellationToken))
        {
            logger.LogError("Bootstrap aborted: a non-administrator account named {Username} already exists", AdminUsername);
            return BootstrapResult.Of(BootstrapStatus.UsernameTaken); // never take it over
        }

        var admin = User.Create(AdminUsername, hasher.Hash(temporaryPassword), UserRole.Admin,
            mustChangePassword: true, clock.GetUtcNow());

        try
        {
            await users.AddAsync(admin, cancellationToken);
        }
        catch (ConflictException)
        {
            logger.LogInformation("Bootstrap lost a concurrent race; another instance created the administrator");
            return BootstrapResult.Of(BootstrapStatus.AlreadyInitialized);
        }

        logger.LogInformation("Bootstrap complete: administrator {UserId} created and must change its password", admin.Id);
        return BootstrapResult.Of(BootstrapStatus.Created);
    }
}
