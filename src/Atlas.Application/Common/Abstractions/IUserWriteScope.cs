namespace Atlas.Application.Common.Abstractions;

public interface IUserWriteScope
{
    /// <summary>
    /// Runs <paramref name="action"/> in a transaction holding a cross-instance exclusive lock, so read-check-write
    /// sequences cannot race: last-administrator protection and all organization changes (department membership, heads, profiles).
    /// </summary>
    Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
