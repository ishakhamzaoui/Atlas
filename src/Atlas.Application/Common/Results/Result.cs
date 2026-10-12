namespace Atlas.Application.Common.Results;

/// <summary>
/// Outcome of a use case. Expected failures (validation, wrong password, duplicate username, ...) are returned as values;
/// exceptions are reserved for genuinely unexpected faults.
/// </summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => Error is not null;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);

    public static implicit operator Result(Error error) => Failure(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T value) : base(null) => _value = value;
    private Result(Error error) : base(error) { }

    /// <summary>The success value. Throws if the result is a failure (a programming error: check IsSuccess first).</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Result is a failure ({Error!.Code}); it has no value.");

    public static Result<T> Success(T value) => new(value);
    public static new Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure(error);
}
