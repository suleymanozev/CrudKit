namespace CrudKit.Core.Models;

/// <summary>
/// Application-level error. Thrown from handlers and converted to the
/// appropriate HTTP status code by AppErrorFilter.
/// </summary>
public class AppError : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public object? Details { get; }

    private AppError(int statusCode, string code, string message, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public static AppError NotFound(string message = "Kayıt bulunamadı")
        => new(404, "NOT_FOUND", message);

    public static AppError BadRequest(string message)
        => new(400, "BAD_REQUEST", message);

    public static AppError Unauthorized(string message = "Yetkisiz erişim")
        => new(401, "UNAUTHORIZED", message);

    public static AppError Forbidden(string message = "Erişim engellendi")
        => new(403, "FORBIDDEN", message);

    public static AppError Validation(ValidationErrors errors)
        => new(400, "VALIDATION_ERROR", "Validasyon hatası", errors);

    public static AppError Conflict(string message)
        => new(409, "CONFLICT", message);
}
