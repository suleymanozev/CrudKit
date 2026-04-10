using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Models;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (arg is null) return await next(ctx);

        var errors = new ValidationErrors();

        var fluentValidator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (fluentValidator is not null)
        {
            var result = await fluentValidator.ValidateAsync(arg);
            if (!result.IsValid)
                foreach (var failure in result.Errors)
                    errors.Add(failure.PropertyName, failure.ErrorCode, failure.ErrorMessage);
        }
        else
        {
            var validationCtx = new ValidationContext(arg);
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(arg, validationCtx, results, validateAllProperties: true))
                foreach (var vr in results)
                    errors.Add(vr.MemberNames.FirstOrDefault() ?? "unknown", "INVALID", vr.ErrorMessage ?? "Invalid value.");
        }

        if (!errors.IsEmpty)
            return Results.Json(new { status = 400, code = "VALIDATION_ERROR", message = "Validation failed.", details = errors.Errors.Select(e => new { e.Field, e.Code, e.Message }) }, statusCode: 400);

        return await next(ctx);
    }
}
