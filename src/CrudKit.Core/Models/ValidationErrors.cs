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
}
