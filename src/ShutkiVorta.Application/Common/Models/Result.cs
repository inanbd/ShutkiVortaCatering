namespace ShutkiVorta.Application.Common.Models;

/// <summary>Outcome of an operation whose failure is an expected, user-facing situation (not an exception).</summary>
public class Result
{
    protected Result(bool succeeded, IEnumerable<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors.ToArray();
    }

    public bool Succeeded { get; }
    public IReadOnlyList<string> Errors { get; }

    public static Result Success() => new(true, []);
    public static Result Failure(params string[] errors) => new(false, errors);
    public static Result Failure(IEnumerable<string> errors) => new(false, errors);
}

public sealed class Result<T> : Result
{
    private Result(bool succeeded, T? value, IEnumerable<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, []);
    public static new Result<T> Failure(params string[] errors) => new(false, default, errors);
    public static new Result<T> Failure(IEnumerable<string> errors) => new(false, default, errors);
}
