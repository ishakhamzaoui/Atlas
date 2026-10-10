using Atlas.Application.Bootstrap;

namespace Atlas.API.Bootstrap;

/// <summary>
/// Handles <c>dotnet run --project src/Atlas.API -- --bootstrap-admin</c>. Reads the temporary password from
/// <c>Bootstrap:AdminPassword</c> (env <c>Bootstrap__AdminPassword</c>), creates the <c>admin</c> account
/// once, prints a status line (never the secret) and exits. The normal web host never reads that setting.
/// </summary>
public static class AdminBootstrapRunner
{
    public const string PasswordSettingKey = "Bootstrap:AdminPassword";

    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();

        var result = await bootstrapper.RunAsync(configuration[PasswordSettingKey], CancellationToken.None);

        switch (result.Status)
        {
            case BootstrapStatus.Created:
                Console.WriteLine("Bootstrap complete: administrator 'admin' created. " +
                                  "It must change its password at first login. Unset Bootstrap__AdminPassword now.");
                return 0;
            case BootstrapStatus.AlreadyInitialized:
                Console.WriteLine("Bootstrap skipped: an administrator already exists. No changes were made.");
                return 0;
            case BootstrapStatus.MissingPassword:
                Console.Error.WriteLine($"Bootstrap failed: {PasswordSettingKey} is not set (env: Bootstrap__AdminPassword). Nothing was created.");
                return 2;
            case BootstrapStatus.WeakPassword:
                Console.Error.WriteLine("Bootstrap failed: the temporary password does not meet the password policy:");
                foreach (var error in result.Errors) Console.Error.WriteLine($" - {error}");
                return 3;
            case BootstrapStatus.UsernameTaken:
                Console.Error.WriteLine("Bootstrap failed: a non-administrator account named 'admin' already exists. It was not modified.");
                return 4;
            default:
                return 1;
        }
    }
}
