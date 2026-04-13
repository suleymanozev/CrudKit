---
sidebar_position: 2
title: Auto Registration
---

# Auto Registration

`UseCrudKit()` automatically scans all loaded assemblies for types decorated with `[CrudEntity]` and registers their CRUD endpoints at startup. No additional packages or code generation required.

## How It Works

When `app.UseCrudKit()` is called, it:

1. Runs any `IModule.MapEndpoints()` registrations first (manual/module-based)
2. Scans all loaded assemblies for `[CrudEntity]`-decorated entity types
3. Skips entities already registered by modules or manual `MapCrudEndpoints` calls
4. For each unregistered entity, checks if `[CreateDtoFor]` / `[UpdateDtoFor]` DTOs exist in the entity's assembly
5. Registers CRUD endpoints using the discovered DTOs, or falls back to entity-as-DTO mode

## Usage

```csharp
var app = builder.Build();
app.UseCrudKit(); // auto-registers all [CrudEntity] types
app.Run();
```

That's it. Every entity decorated with `[CrudEntity]` gets full CRUD endpoints automatically.

## Entity as DTO

If no DTOs are provided for an entity, `UseCrudKit` uses the entity itself as both the Create and Update DTO. System fields (`Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `TenantId`, etc.) are automatically skipped during mapping — only user-defined properties are written.

## Manual DTOs — [CreateDtoFor] / [UpdateDtoFor] / [ResponseDtoFor]

Annotate hand-written DTOs with the appropriate attribute so CrudKit can wire them up automatically:

| Attribute | Purpose |
|-----------|---------|
| `[CreateDtoFor(typeof(TEntity))]` | Marks the Create DTO for an entity |
| `[UpdateDtoFor(typeof(TEntity))]` | Marks the Update DTO for an entity |
| `[ResponseDtoFor(typeof(TEntity))]` | Marks the response DTO; use with `IResponseMapper` for custom response shapes |

```csharp
[CreateDtoFor(typeof(Order))]
public record CreateOrder([Required] string CustomerName, decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
}

[ResponseDtoFor(typeof(Order))]
public class OrderResponse
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; }
    public string StatusLabel { get; set; } // computed field
}
```

## Manual Registration

You can still register entities manually with `MapCrudEndpoints` if you need full control. Auto-scan skips entities already registered this way:

```csharp
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products")
    .Authorize(auth => auth.RequireRole("Admin"));

app.UseCrudKit(); // skips Product — already registered above
app.Run();
```

## Extending with IEndpointConfigurer

Implement `IEndpointConfigurer<TEntity>` to customize auto-registered endpoints. The configurer is discovered and applied automatically:

```csharp
public class ProductEndpointConfigurer : IEndpointConfigurer<Product>
{
    public void Configure(CrudEndpointGroup<Product> group)
    {
        group.Authorize(auth => auth.RequireRole("Admin"));
    }
}
```

## Notes

- Auto-scan only finds entities in assemblies that are already loaded at the time `UseCrudKit()` is called.
- If you prefer fully manual registration, simply call `MapCrudEndpoints` for each entity before `UseCrudKit()` — auto-scan will skip them all.
- You can mix auto-registered and manually registered entities in the same application.
