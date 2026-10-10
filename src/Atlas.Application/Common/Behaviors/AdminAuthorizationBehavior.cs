using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using MediatR;

namespace Atlas.Application.Common.Behaviors;

/// <summary>Defense in depth: enforces administrator-only use cases even if HTTP-level policies are misconfigured.</summary>
public sealed class AdminAuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IRequireAdmin)
        {
            if (!currentUser.IsAuthenticated)
                throw new UnauthorizedException("unauthenticated", "Authentication is required.");
            if (!currentUser.IsAdmin)
                throw new ForbiddenException();
        }

        return next();
    }
}
