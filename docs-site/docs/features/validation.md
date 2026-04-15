---
sidebar_position: 6
title: Validation
---

# Validation

CrudKit uses FluentValidation as the primary validation mechanism, with DataAnnotations as the fallback.

## How It Works

FluentValidation validators are resolved from DI and run first. If none is registered for a DTO, DataAnnotation attributes on the DTO or entity are evaluated instead.

Validation errors return `400` with a structured response:

```json
{
  "status": 400,
  "code": "VALIDATION_ERROR",
  "errors": [
    { "field": "Name", "message": "The Name field is required." }
  ]
}
```

## Registering a FluentValidation Validator

```csharp
public class CreateProductValidator : AbstractValidator<CreateProduct>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().Matches("^[A-Z0-9-]+$");
        RuleFor(x => x.Price).GreaterThan(0);
    }
}

builder.Services.AddScoped<IValidator<CreateProduct>, CreateProductValidator>();
```

### Update DTO Validator

Update DTOs use `Optional<T>` for partial updates — validate only when the field is provided:

```csharp
public record UpdateProduct
{
    public Optional<string?> Name { get; init; }
    public Optional<decimal?> Price { get; init; }
}

public class UpdateProductValidator : AbstractValidator<UpdateProduct>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name.Value)
            .NotEmpty().MaximumLength(200)
            .When(x => x.Name.HasValue);

        RuleFor(x => x.Price.Value)
            .GreaterThan(0)
            .When(x => x.Price.HasValue);
    }
}
```

### Cross-Field and Async Validation

```csharp
public class CreateInvoiceValidator : AbstractValidator<CreateInvoice>
{
    public CreateInvoiceValidator(AppDbContext db)
    {
        RuleFor(x => x.DueDate)
            .GreaterThan(x => x.InvoiceDate)
            .WithMessage("Due date must be after invoice date.");

        RuleFor(x => x.AssociateId)
            .MustAsync(async (id, ct) => await db.Associates.AnyAsync(a => a.Id == id, ct))
            .WithMessage("Associate not found.");
    }
}
```

### Registering All Validators

To register all validators in an assembly at once, use `AddValidatorsFromAssembly` — this is the application's responsibility, not CrudKit's:

```csharp
builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);
```

## DataAnnotations Fallback

When no FluentValidation validator is registered, DataAnnotations on the DTO are checked:

```csharp
public record CreateProduct(
    [Required, MaxLength(200)] string Name,
    [Range(0.01, 999_999.99)] decimal Price,
    [Required, MaxLength(50)] string Sku
);
```

## Startup Validation

`CrudKitStartupValidator` runs as an `IHostedService` at startup and validates entity metadata before the first request:

- `[CrudEntity(OwnerField = "X")]` — verifies property `X` exists on the entity. Throws if missing.
- `IConcurrent` + `EnableBulkUpdate` — logs a warning (bulk updates bypass optimistic concurrency).

Startup failures are detected early, before any traffic reaches the application.
