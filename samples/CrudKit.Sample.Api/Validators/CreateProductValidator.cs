using CrudKit.Sample.Api.Dtos;
using FluentValidation;

namespace CrudKit.Sample.Api.Validators;

/// <summary>
/// FluentValidation validator for CreateProduct.
/// When registered in DI, this takes precedence over DataAnnotation attributes.
/// </summary>
public class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.")
            .MinimumLength(3).WithMessage("Product name must be at least 3 characters.")
            .MaximumLength(200).WithMessage("Product name must not exceed 200 characters.");

        RuleFor(x => x.Price)
            .GreaterThan(0).WithMessage("Price must be positive.")
            .LessThanOrEqualTo(999_999.99m).WithMessage("Price must not exceed 999,999.99.");

        RuleFor(x => x.Sku)
            .NotEmpty().WithMessage("SKU is required.")
            .Matches(@"^[A-Z0-9\-]+$").WithMessage("SKU must contain only uppercase letters, digits, and dashes.");
    }
}
