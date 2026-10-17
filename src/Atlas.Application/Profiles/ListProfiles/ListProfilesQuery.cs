using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Domain.Organization;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Profiles.ListProfiles;

/// <param name="Assigned">true: held by a user; false: unassigned (retained for future use); null: all.</param>
public sealed record ListProfilesQuery(int Page, int PageSize, string? Search, bool? Assigned = null)
    : IRequest<Result<PagedResult<ProfileDto>>>, IRequireAdmin;

public sealed class ListProfilesQueryValidator : AbstractValidator<ListProfilesQuery>
{
    public const int MaxPageSize = 100;

    public ListProfilesQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(OrganizationNamePolicy.MaxLength);
    }
}

public sealed class ListProfilesQueryHandler(IProfileRepository profiles)
    : IRequestHandler<ListProfilesQuery, Result<PagedResult<ProfileDto>>>
{
    public async Task<Result<PagedResult<ProfileDto>>> Handle(ListProfilesQuery request, CancellationToken cancellationToken)
    {
        var page = await profiles.ListAsync(
            new ProfileListFilter(request.Page, request.PageSize, request.Search, request.Assigned), cancellationToken);
        return page.Map(ProfileDto.From);
    }
}
