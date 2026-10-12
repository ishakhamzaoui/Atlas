using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
using MediatR;

namespace Atlas.Application.Common.Behaviors;

/// <summary>Defense in depth: enforces administrator-only use cases even if HTTP-level policies are misconfigured.</summary>
public sealed class AdminAuthorizationBehavior<TRequest, TResponse>(ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is IRequireAdmin)
        {
            if (!currentUser.IsAuthenticated)
                return Task.FromResult(FailureFactory<TResponse>.Create(Error.Unauthenticated()));
            if (!currentUser.IsAdmin)
                return Task.FromResult(FailureFactory<TResponse>.Create(Error.Forbidden()));
        }

        return next();
    }
}
