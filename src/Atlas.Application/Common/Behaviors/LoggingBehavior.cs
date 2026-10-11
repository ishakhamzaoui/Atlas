using System.Diagnostics;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Common.Behaviors;

/// <summary>
/// Leveled request logging. Only the request TYPE NAME is logged, never its content (commands carry passwords/tokens).
/// DEBUG: start/finish and timing. WARNING: expected rejections (validation, 4xx-style). ERROR: unexpected failures.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
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
            logger.LogDebug("Handled {RequestName} in {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("{RequestName} failed validation on [{Properties}] after {ElapsedMs} ms",
                name, string.Join(", ", ex.Errors.Select(e => e.PropertyName).Distinct()), stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (AppException ex)
        {
            logger.LogWarning("{RequestName} rejected with {ErrorCode} after {ElapsedMs} ms (caller {UserId})",
                name, ex.Code, stopwatch.ElapsedMilliseconds, currentUser.UserId);
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("{RequestName} was cancelled after {ElapsedMs} ms", name, stopwatch.ElapsedMilliseconds);
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
