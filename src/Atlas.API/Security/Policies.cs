namespace Atlas.API.Security;

public static class Policies
{
    /// <summary>Any valid token, including one restricted to the forced password change.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Valid token AND no pending forced password change. This is also the default and fallback policy.</summary>
    public const string FullAccess = "FullAccess";

    /// <summary>Administrator with no pending forced password change.</summary>
    public const string AdminOnly = "AdminOnly";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
}
