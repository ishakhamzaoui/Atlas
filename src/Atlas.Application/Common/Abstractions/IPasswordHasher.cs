namespace Atlas.Application.Common.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>Returns false (never throws) for wrong passwords and malformed hashes.</summary>
    bool Verify(string password, string passwordHash);

    /// <summary>A valid hash nobody knows the password for; verify against it to equalise timing for unknown users.</summary>
    string DummyHash { get; }
}
