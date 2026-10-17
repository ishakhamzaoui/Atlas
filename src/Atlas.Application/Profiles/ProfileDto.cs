using Atlas.Application.Common.Abstractions;

namespace Atlas.Application.Profiles;

public sealed record ProfileDto(
    Guid Id,
    string Name,
    string? Description,
    int AssignedUserCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ProfileDto From(ProfileSummary s) =>
        new(s.Profile.Id, s.Profile.Name, s.Profile.Description, s.AssignedUserCount, s.Profile.CreatedAt, s.Profile.UpdatedAt);
}
