namespace Atlas.Application.Common.Exceptions;

/// <summary>Base type for expected, client-visible failures. Messages are authored and safe to return.</summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string message = "The requested resource was not found.")
    : AppException("not_found", message);

public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>A business rule forbids the operation (for example removing the last active administrator).</summary>
public sealed class BusinessRuleException(string code, string message) : AppException(code, message);

public sealed class ForbiddenException(string message = "You do not have permission to perform this action.")
    : AppException("forbidden", message);

public sealed class UnauthorizedException(string code = "invalid_credentials", string message = "Authentication failed.")
    : AppException(code, message);
