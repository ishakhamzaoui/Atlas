namespace Atlas.Application.Common;

/// <summary>Claim names shared by the token issuer (Infrastructure) and the API.</summary>
public static class AuthClaims
{
    public const string Subject = "sub";
    public const string UniqueName = "unique_name";
    public const string Role = "role";
    public const string SecurityStamp = "sst";

    /// <summary>Present (value "true") only on tokens issued to accounts that must change their password first.</summary>
    public const string MustChangePassword = "pwd_chg";
}
