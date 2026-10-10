namespace Atlas.Domain.Users;

/// <summary>Password strength rules (applied to new passwords only, never to login attempts).</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    public static IReadOnlyList<string> Validate(string? password, string? username = null)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Password is required.");
            return errors;
        }

        if (password.Length < MinLength)
            errors.Add($"Password must be at least {MinLength} characters long.");
        if (password.Length > MaxLength)
            errors.Add($"Password must be at most {MaxLength} characters long.");
        if (!password.Any(char.IsLower))
            errors.Add("Password must contain a lowercase letter.");
        if (!password.Any(char.IsUpper))
            errors.Add("Password must contain an uppercase letter.");
        if (!password.Any(char.IsDigit))
            errors.Add("Password must contain a digit.");
        if (!password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
            errors.Add("Password must contain a symbol.");
        if (!string.IsNullOrWhiteSpace(username) &&
            password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            errors.Add("Password must not contain the username.");

        return errors;
    }
}
