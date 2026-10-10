using System.Security.Cryptography;
using Atlas.Application.Common.Abstractions;
using BC = BCrypt.Net.BCrypt;

namespace Atlas.Infrastructure.Security;

/// <summary>
/// BCrypt (via BCrypt.Net-Next) using the "enhanced" mode, which pre-hashes with SHA-384 so passwords
/// longer than bcrypt's 72-byte limit are not silently truncated.
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public const int DefaultWorkFactor = 12;

    private readonly int _workFactor;

    public BCryptPasswordHasher(int workFactor = DefaultWorkFactor)
    {
        if (workFactor is < 4 or > 31) throw new ArgumentOutOfRangeException(nameof(workFactor));
        _workFactor = workFactor;
        DummyHash = BC.EnhancedHashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)), _workFactor);
    }

    public string DummyHash { get; }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return BC.EnhancedHashPassword(password, _workFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash)) return false;
        try
        {
            return BC.EnhancedVerify(password, passwordHash);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
