namespace Atlas.Application.Common.Abstractions;

/// <summary>The authenticated caller, derived from the validated token only.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Username { get; }
    bool IsAdmin { get; }
}
