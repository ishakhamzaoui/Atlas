namespace Atlas.Application.Common;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
