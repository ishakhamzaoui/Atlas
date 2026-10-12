using System.Reflection;

namespace Atlas.Application.Common.Results;

/// <summary>Lets generic pipeline behaviors short-circuit with a failure of whatever Result type the request returns.</summary>
internal static class FailureFactory<TResponse> where TResponse : Result
{
    private static readonly MethodInfo FailureMethod = typeof(TResponse).GetMethod(
        nameof(Result.Failure),
        BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
        [typeof(Error)])
        ?? throw new InvalidOperationException($"{typeof(TResponse).Name} must declare a static Failure(Error) method.");

    public static TResponse Create(Error error) => (TResponse)FailureMethod.Invoke(null, [error])!;
}
