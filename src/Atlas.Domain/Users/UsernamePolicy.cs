using System.Text.RegularExpressions;

namespace Atlas.Domain.Users;

/// <summary>Username rules and the single normalization used for case-insensitive uniqueness.</summary>
public static class UsernamePolicy
{
    public const int MinLength = 3;
    public const int MaxLength = 32;

    private static readonly Regex AllowedPattern =
        new(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Trim + upper-invariant. Usernames are ASCII-only, so this is stable and matches PostgreSQL upper().</summary>
    public static string Normalize(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        return username.Trim().ToUpperInvariant();
    }

    public static IReadOnlyList<string> Validate(string? username)
    {
        var errors = new List<string>();
        var value = username?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            errors.Add("Username is required.");
            return errors;
        }

        if (value.Length < MinLength || value.Length > MaxLength)
            errors.Add($"Username must be between {MinLength} and {MaxLength} characters.");

        if (!AllowedPattern.IsMatch(value))
            errors.Add("Username may only contain letters, digits, '.', '_' and '-', and must start with a letter or digit.");

        return errors;
    }
}
