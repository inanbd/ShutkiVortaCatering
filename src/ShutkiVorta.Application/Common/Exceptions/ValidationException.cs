using FluentValidation.Results;

namespace ShutkiVorta.Application.Common.Exceptions;

/// <summary>One or more request values are invalid. Keys are property names ("" for errors not tied to a field).</summary>
public sealed class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(string message)
        : this(string.Empty, message)
    {
    }

    public ValidationException(string propertyName, string message)
        : base(message)
    {
        Errors = new Dictionary<string, string[]> { [propertyName] = [message] };
    }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : this()
    {
        Errors = failures
            .GroupBy(f => f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
