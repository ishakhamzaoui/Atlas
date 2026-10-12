using System.Text.Json;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Common.Behaviors;

/// <summary>Runs FluentValidation and returns a validation failure (no exception) when the request is invalid.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)));

            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
            if (failures.Count != 0)
            {
                var errors = failures
                    .GroupBy(f => JsonNamingPolicy.CamelCase.ConvertName(f.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());

                return FailureFactory<TResponse>.Create(Error.Validation(errors));
            }
        }

        return await next();
    }
}
