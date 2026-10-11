namespace Atlas.Application.Common.Abstractions;

public sealed record GeneratedRefreshToken(string Token, string Hash);

public interface IRefreshTokenProvider
{
    /// <summary>Sliding lifetime of each refresh token.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>Maximum lifetime of a whole session family, regardless of rotation.</summary>
    TimeSpan AbsoluteLifetime { get; }

    /// <summary>Creates a high-entropy opaque token and the hash that is persisted instead of it.</summary>
    GeneratedRefreshToken Generate();

    string Hash(string token);
}
