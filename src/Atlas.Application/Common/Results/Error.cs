namespace Atlas.Application.Common.Results;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    BusinessRule
}

/// <summary>An expected, client-visible failure. Never carries secrets; messages are authored and safe to return.</summary>
/// <param name="Code">Stable machine-readable code (for example <c>username_taken</c>).</param>
/// <param name="ValidationErrors">Field name (camelCase) to messages; only for <see cref="ErrorType.Validation"/>.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new("validation_failed", "One or more validation errors occurred.", ErrorType.Validation, errors);

    public static Error Unauthorized(string code = "invalid_credentials", string message = "Authentication failed.") =>
        new(code, message, ErrorType.Unauthorized);

    public static Error Unauthenticated() =>
        new("unauthenticated", "Authentication is required.", ErrorType.Unauthorized);

    public static Error Forbidden(string message = "You do not have permission to perform this action.") =>
        new("forbidden", message, ErrorType.Forbidden);

    public static Error NotFound(string message = "The requested resource was not found.") =>
        new("not_found", message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
}
