using System.Text.Json;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Errors;

/// <summary>Maps application/domain exceptions to consistent RFC 7807 problem responses with a stable "code".</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };
                problem.Extensions["code"] = "validation_failed";
                break;

            case AppException app:
                problem = new ProblemDetails
                {
                    Status = app switch
                    {
                        NotFoundException => StatusCodes.Status404NotFound,
                        ConflictException => StatusCodes.Status409Conflict,
                        BusinessRuleException => StatusCodes.Status422UnprocessableEntity,
                        ForbiddenException => StatusCodes.Status403Forbidden,
                        UnauthorizedException => StatusCodes.Status401Unauthorized,
                        _ => StatusCodes.Status400BadRequest
                    },
                    Title = app.Message
                };
                problem.Extensions["code"] = app.Code;
                break;

            case DomainException domain:
                problem = new ProblemDetails { Status = StatusCodes.Status422UnprocessableEntity, Title = domain.Message };
                problem.Extensions["code"] = domain.Code;
                break;

            default:
                // Faults inside MediatR requests are already logged (with the exception) by LoggingBehavior.
                if (exception.Data.Contains(LoggingBehavior<object, object>.LoggedMarker))
                    logger.LogDebug("Unexpected failure for {Method} {Path} was already logged", httpContext.Request.Method, httpContext.Request.Path);
                else
                    logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred."
                };
                problem.Extensions["code"] = "internal_error";
                break;
        }

        problem.Instance = httpContext.Request.Path;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
        return true;
    }
}
