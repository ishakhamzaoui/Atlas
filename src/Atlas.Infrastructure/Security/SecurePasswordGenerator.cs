using System.Security.Cryptography;
using Atlas.Application.Common.Abstractions;

namespace Atlas.Infrastructure.Security;

public sealed class SecurePasswordGenerator : IPasswordGenerator
{
    private const int Length = 20;
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";   // no 'l'
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";    // no 'I', 'O'
    private const string Digits = "23456789";                   // no 0/1
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string All = Lower + Upper + Digits + Symbols;

    public string Generate()
    {
        var chars = new char[Length];
        chars[0] = Pick(Lower);
        chars[1] = Pick(Upper);
        chars[2] = Pick(Digits);
        chars[3] = Pick(Symbols);
        for (var i = 4; i < Length; i++) chars[i] = Pick(All);

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
