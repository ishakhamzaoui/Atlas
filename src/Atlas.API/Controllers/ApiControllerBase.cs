using Atlas.API.Errors;
using Atlas.Application.Common.Results;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Controllers;

/// <summary>Maps use-case <see cref="Result"/>s to HTTP: success to the given response, failure to a problem+json response.</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Fail(Error error)
    {
        var problem = ProblemMapping.ToProblemDetails(error, HttpContext);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" }
        };
    }

    protected ActionResult<T> Respond<T>(Result<T> result)
    {
        if (result.IsFailure) return Fail(result.Error!);
        return Ok(result.Value);
    }

    protected IActionResult Respond(Result result, Func<IActionResult> onSuccess) =>
        result.IsFailure ? Fail(result.Error!) : onSuccess();
}
