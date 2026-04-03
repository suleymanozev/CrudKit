namespace CrudKit.Core.Models;

public class ValidationErrors
{
    public List<FieldError> Errors { get; set; } = new();

    public bool IsEmpty => Errors.Count == 0;

    public void Add(string field, string code, string message)
        => Errors.Add(new FieldError(field, code, message));

    public void ThrowIfInvalid()
    {
        if (!IsEmpty)
            throw AppError.Validation(this);
    }
}
