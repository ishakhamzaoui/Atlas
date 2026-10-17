namespace Atlas.Domain.Organization;

/// <summary>Shared naming rules for departments and profiles (same cleaning and case-insensitive uniqueness as usernames).</summary>
public static class OrganizationNamePolicy
{
    public const int MinLength = 2;
    public const int MaxLength = 100;
    public const int MaxDescriptionLength = 500;

    /// <summary>Trims and collapses internal whitespace, so "Sales  North " and "Sales North" are the same name.</summary>
    public static string Clean(string name) =>
        string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Normalize(string name) => Clean(name).ToUpperInvariant();

    public static IReadOnlyList<string> ValidateName(string? name)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Name is required.");
            return errors;
        }

        var cleaned = Clean(name);
        if (cleaned.Length < MinLength || cleaned.Length > MaxLength)
            errors.Add($"Name must be between {MinLength} and {MaxLength} characters.");
        if (cleaned.Any(char.IsControl))
            errors.Add("Name must not contain control characters.");
        return errors;
    }

    public static IReadOnlyList<string> ValidateDescription(string? description)
    {
        var errors = new List<string>();
        if (description is null) return errors;
        if (description.Length > MaxDescriptionLength)
            errors.Add($"Description must be at most {MaxDescriptionLength} characters.");
        return errors;
    }
}
