using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Profiles.GetProfile;

public sealed record GetProfileQuery(Guid Id) : IRequest<Result<ProfileDto>>, IRequireAdmin;

public sealed class GetProfileQueryValidator : AbstractValidator<GetProfileQuery>
{
    public GetProfileQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetProfileQueryHandler(IProfileRepository profiles) : IRequestHandler<GetProfileQuery, Result<ProfileDto>>
{
    public async Task<Result<ProfileDto>> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var summary = await profiles.GetSummaryAsync(request.Id, cancellationToken);
        return summary is null ? Error.NotFound("Profile not found.") : ProfileDto.From(summary);
    }
}
