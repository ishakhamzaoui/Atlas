namespace Atlas.Application.Common.Abstractions;

public interface IUserSessionValidator
{
    /// <summary>True when the user still exists, is active and the token's security stamp is current.</summary>
    Task<bool> IsSessionValidAsync(Guid userId, Guid securityStamp, CancellationToken cancellationToken);
}
