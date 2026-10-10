namespace Atlas.Application.Common.Abstractions;

public interface IUserWriteScope
{
    /// <summary>
    /// Runs <paramref name="action"/> in a transaction holding a cross-instance exclusive lock, so
    /// "check administrator count, then change" sequences cannot race (last-administrator protection).
    /// </summary>
    Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
}
