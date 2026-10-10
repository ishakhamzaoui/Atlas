using Atlas.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Persistence.Repositories;

public sealed class UserWriteScope(AtlasDbContext db) : IUserWriteScope
{
    // Arbitrary constant key for the "administrator set" advisory lock ("ATLAS" + 01).
    private const long AdminSetLockKey = 0x41544C4153_01;

    public async Task<T> RunExclusiveAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Transaction-scoped: released automatically on commit/rollback, works across app instances.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminSetLockKey})", cancellationToken);

        var result = await action(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
