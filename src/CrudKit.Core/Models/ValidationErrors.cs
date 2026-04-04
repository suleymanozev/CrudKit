namespace CrudKit.Core.Models;

public class ValidationErrors
{
    private readonly List<FieldError> _errors = new();
    public IReadOnlyList<FieldError> Errors => _errors;

    public bool IsEmpty => _errors.Count == 0;

    public void Add(string field, string code, string message)
        => _errors.Add(new FieldError(field, code, message));

    public void ThrowIfInvalid()
    {
        if (!IsEmpty)
            throw AppError.Validation(this);
    }

    /// <summary>
    /// Converts errors to the format expected by Results.ValidationProblem().
    /// Groups errors by field name, each field maps to an array of error messages.
    /// </summary>
    public Dictionary<string, string[]> ToDictionary()
    {
        return _errors
            .GroupBy(e => e.Field)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.Message).ToArray());
    }
}
