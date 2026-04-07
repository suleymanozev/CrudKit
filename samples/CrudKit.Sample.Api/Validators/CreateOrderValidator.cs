using CrudKit.Sample.Api.Dtos;
using FluentValidation;

namespace CrudKit.Sample.Api.Validators;

/// <summary>
/// FluentValidation validator for CreateOrder.
/// Demonstrates business rule validation beyond simple DataAnnotations.
/// </summary>
public class CreateOrderValidator : AbstractValidator<CreateOrder>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.CustomerName)
            .NotEmpty().WithMessage("Customer name is required.")
            .MinimumLength(2).WithMessage("Customer name must be at least 2 characters.");

        RuleFor(x => x.Total)
            .GreaterThanOrEqualTo(0).WithMessage("Order total cannot be negative.");
    }
}
