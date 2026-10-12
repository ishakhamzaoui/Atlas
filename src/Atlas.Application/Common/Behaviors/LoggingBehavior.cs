using System.Diagnostics;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Common.Behaviors;

/// <summary>
/// Leveled request logging. Only the request TYPE NAME is logged, never its content (commands carry passwords/tokens).
/// DEBUG: start/finish and timing. WARNING: expected failures returned as <see cref="Result"/> (no stack trace, they are normal).
/// ERROR: unexpected exceptions, with the exception.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public const string LoggedMarker = "atlas.logged";

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();
        logger.LogDebug("Handling {RequestName} (caller {UserId})", name, currentUser.UserId);

        try
        {
            var response = await next();

            if (response.Error is { } error)
            {
                if (error.Type == ErrorType.Validation)
                    logger.LogWarning("{RequestName} failed validation on [{Properties}] after {ElapsedMs} ms",
                        name, string.Join(", ", error.ValidationErrors?.Keys ?? []), stopwatch.ElapsedMilliseconds);
                else
                    logger.LogWarning("{RequestName} rejected with {ErrorCode} ({ErrorType}) after {ElapsedMs} ms (caller {UserId})",
                        name, error.Code, error.Type, stopwatch.ElapsedMilliseconds, currentUser.UserId);
            }
            else
            {
                logger.LogDebug("Handled {RequestName} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("{RequestName} was cancelled after {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (PersistenceConflictException ex)
        {
            // A database-arbitrated race is expected under concurrency: warn without a stack trace; the API turns it into a 409.
            logger.LogWarning("{RequestName} lost a persistence race ({Kind}) after {ElapsedMs} ms", name, ex.Kind, stopwatch.ElapsedMilliseconds);
            ex.Data[LoggedMarker] = true;
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{RequestName} failed unexpectedly after {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            ex.Data[LoggedMarker] = true; // lets the HTTP exception handler avoid logging the same fault twice
            throw;
        }
    }
}
