using System.Text.Json;
using Atlas.Application.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Errors;

/// <summary>The single place that turns an application <see cref="Error"/> into an RFC 7807 response.</summary>
public static class ProblemMapping
{
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError
    };

    public static ProblemDetails ToProblemDetails(Error error, HttpContext httpContext)
    {
        ProblemDetails problem = error.ValidationErrors is { } validation
            ? new ValidationProblemDetails(validation.ToDictionary(kv => kv.Key, kv => kv.Value))
            : new ProblemDetails();

        problem.Status = StatusCodeFor(error.Type);
        problem.Title = error.Message;
        problem.Instance = httpContext.Request.Path;
        problem.Extensions["code"] = error.Code;
        return problem;
    }

    public static ProblemDetails Create(int status, string title, string code, HttpContext httpContext)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Instance = httpContext.Request.Path };
        problem.Extensions["code"] = code;
        return problem;
    }

    public static Task WriteAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return httpContext.Response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, "application/problem+json", cancellationToken);
    }
}
