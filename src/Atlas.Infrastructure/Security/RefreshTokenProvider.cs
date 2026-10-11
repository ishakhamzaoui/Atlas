using System.Security.Cryptography;
using System.Text;
using Atlas.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure.Security;

/// <summary>
/// Opaque 256-bit random tokens (base64url). Only the SHA-256 hash is stored, so a database leak does not
/// yield usable tokens. A fast hash is appropriate here because the input already has 256 bits of entropy.
/// </summary>
public sealed class RefreshTokenProvider(IOptions<JwtOptions> options) : IRefreshTokenProvider
{
    private const int TokenBytes = 32;

    public TimeSpan Lifetime { get; } = TimeSpan.FromDays(options.Value.RefreshTokenDays);
    public TimeSpan AbsoluteLifetime { get; } = TimeSpan.FromDays(options.Value.RefreshTokenAbsoluteDays);

    public GeneratedRefreshToken Generate()
    {
        var token = ToBase64Url(RandomNumberGenerator.GetBytes(TokenBytes));
        return new GeneratedRefreshToken(token, Hash(token));
    }

    public string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
