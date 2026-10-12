namespace Atlas.Application.Common.Exceptions;

public enum PersistenceConflictKind
{
    DuplicateUsername,
    ConcurrentUpdate
}

/// <summary>
/// Raised by the persistence layer when the database arbitrates a race (unique index or optimistic concurrency).
/// Handlers that know what to do translate it into an <c>Error</c>; otherwise the API maps it to 409.
/// </summary>
public sealed class PersistenceConflictException(PersistenceConflictKind kind)
    : Exception(kind == PersistenceConflictKind.DuplicateUsername
        ? "That username is already in use."
        : "The record was modified by someone else. Reload and try again.")
{
    public PersistenceConflictKind Kind { get; } = kind;
}
