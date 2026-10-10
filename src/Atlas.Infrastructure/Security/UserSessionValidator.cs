using Atlas.Application.Common.Abstractions;
using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Infrastructure.Security;

public sealed class UserSessionValidator(AtlasDbContext db) : IUserSessionValidator
{
    public Task<bool> IsSessionValidAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsActive && u.SecurityStamp == securityStamp, cancellationToken);
}
