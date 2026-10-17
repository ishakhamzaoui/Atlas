using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Organization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Profiles.CreateProfile;

public sealed record CreateProfileCommand(string Name, string? Description) : IRequest<Result<ProfileDto>>, IRequireAdmin;

public sealed class CreateProfileCommandValidator : AbstractValidator<CreateProfileCommand>
{
    public CreateProfileCommandValidator()
    {
        RuleFor(x => x.Name).MustBeValidOrganizationName();
        RuleFor(x => x.Description).MustBeValidDescription();
    }
}

public sealed class CreateProfileCommandHandler(
    IProfileRepository profiles,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<CreateProfileCommandHandler> logger) : IRequestHandler<CreateProfileCommand, Result<ProfileDto>>
{
    public Task<Result<ProfileDto>> Handle(CreateProfileCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<ProfileDto>>(async ct =>
        {
            if (await profiles.NameExistsAsync(OrganizationNamePolicy.Normalize(request.Name), null, ct))
                return Error.Conflict("profile_name_taken", "A profile with that name already exists.");

            var profile = Profile.Create(request.Name, request.Description, clock.GetUtcNow());
            try
            {
                await profiles.AddAsync(profile, ct);
            }
            catch (PersistenceConflictException ex)
            {
                return PersistenceErrors.From(ex);
            }

            logger.LogInformation("Admin {ActorId} created profile {ProfileId}", currentUser.UserId, profile.Id);
            return ProfileDto.From(new ProfileSummary(profile, 0));
        }, cancellationToken);
}
