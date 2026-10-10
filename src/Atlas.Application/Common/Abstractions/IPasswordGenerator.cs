namespace Atlas.Application.Common.Abstractions;

public interface IPasswordGenerator
{
    /// <summary>Cryptographically random password that satisfies the password policy.</summary>
    string Generate();
}
