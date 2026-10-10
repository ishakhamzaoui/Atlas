namespace Atlas.Domain.Common;

/// <summary>Raised when a domain invariant is violated.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
