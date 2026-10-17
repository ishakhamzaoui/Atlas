using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Organization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Profiles.UpdateProfile;

public sealed record UpdateProfileCommand(Guid Id, string Name, string? Description) : IRequest<Result<ProfileDto>>, IRequireAdmin;

public sealed class UpdateProfileCommandValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).MustBeValidOrganizationName();
        RuleFor(x => x.Description).MustBeValidDescription();
    }
}

public sealed class UpdateProfileCommandHandler(
    IProfileRepository profiles,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<UpdateProfileCommandHandler> logger) : IRequestHandler<UpdateProfileCommand, Result<ProfileDto>>
{
    public Task<Result<ProfileDto>> Handle(UpdateProfileCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<ProfileDto>>(async ct =>
        {
            var profile = await profiles.GetByIdAsync(request.Id, ct);
            if (profile is null)
                return Error.NotFound("Profile not found.");

            var normalized = OrganizationNamePolicy.Normalize(request.Name);
            if (normalized != profile.NormalizedName && await profiles.NameExistsAsync(normalized, profile.Id, ct))
                return Error.Conflict("profile_name_taken", "A profile with that name already exists.");

            profile.Update(request.Name, request.Description, clock.GetUtcNow());
            try
            {
                await profiles.SaveChangesAsync(ct);
            }
            catch (PersistenceConflictException ex)
            {
                return PersistenceErrors.From(ex);
            }

            logger.LogInformation("Admin {ActorId} updated profile {ProfileId}", currentUser.UserId, profile.Id);
            return ProfileDto.From((await profiles.GetSummaryAsync(profile.Id, ct))!);
        }, cancellationToken);
}
