using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing secret. Never committed; supplied via environment or user-secrets.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public const int MinSigningKeyBytes = 32;

    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer)) failures.Add("Jwt:Issuer is required.");
        if (string.IsNullOrWhiteSpace(options.Audience)) failures.Add("Jwt:Audience is required.");

        if (string.IsNullOrWhiteSpace(options.SigningKey))
            failures.Add("Jwt:SigningKey is required (set the Jwt__SigningKey environment variable or a user-secret).");
        else if (System.Text.Encoding.UTF8.GetByteCount(options.SigningKey) < MinSigningKeyBytes)
            failures.Add($"Jwt:SigningKey must be at least {MinSigningKeyBytes} bytes (use e.g. `openssl rand -base64 48`).");

        if (options.AccessTokenMinutes is < 1 or > 60)
            failures.Add("Jwt:AccessTokenMinutes must be between 1 and 60.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
