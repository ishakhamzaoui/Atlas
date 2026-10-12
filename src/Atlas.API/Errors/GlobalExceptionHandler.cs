using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Atlas.API.Errors;

/// <summary>
/// Safety net for exceptions. Expected failures never get here (use cases return <c>Result</c> values and controllers map them
/// via <see cref="ProblemMapping"/>); only database-arbitrated races, broken invariants and genuine faults do.
/// Only genuine faults are logged as errors with a stack trace.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            PersistenceConflictException conflict => ProblemMapping.Create(
                StatusCodes.Status409Conflict, conflict.Message,
                conflict.Kind == PersistenceConflictKind.DuplicateUsername ? "username_taken" : "concurrent_update", httpContext),

            // A domain invariant tripped although validation should have caught it: the client sent something invalid.
            DomainException domain => ProblemMapping.Create(StatusCodes.Status422UnprocessableEntity, domain.Message, domain.Code, httpContext),

            _ => ProblemMapping.Create(StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "internal_error", httpContext)
        };

        switch (exception)
        {
            case PersistenceConflictException:
                break; // already logged as a warning by the pipeline
            case DomainException:
                logger.LogWarning("Domain rule rejected {Method} {Path}: {Message}", httpContext.Request.Method, httpContext.Request.Path, exception.Message);
                break;
            default:
                // Faults inside MediatR requests were already logged (with the exception) by LoggingBehavior.
                if (exception.Data.Contains(LoggingBehavior<Atlas.Application.Common.Results.Result, Atlas.Application.Common.Results.Result>.LoggedMarker))
                    logger.LogDebug("Unexpected failure for {Method} {Path} was already logged", httpContext.Request.Method, httpContext.Request.Path);
                else
                    logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                break;
        }

        await ProblemMapping.WriteAsync(httpContext, problem, cancellationToken);
        return true;
    }
}
