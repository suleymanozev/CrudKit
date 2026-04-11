---
sidebar_position: 2
title: Source Generation
---

# Source Generation

Add the `CrudKit.SourceGen` package to enable Roslyn-based code generation. The generator scans for all `[CrudEntity]`-decorated classes at compile time.

## Installation

```bash
dotnet add package CrudKit.SourceGen
```

## Generated Files

SourceGen no longer generates DTOs or mappers. It generates two things per project:

| Generated File | Description |
|----------------|-------------|
| `{Entity}Hooks.g.cs` | Empty `ICrudHooks<T>` partial class stub to extend |
| `CrudKitEndpoints.g.cs` | `MapAllCrudEndpoints()` extension method |

DTOs and mappers are written by hand and registered via `[CreateDtoFor]`, `[UpdateDtoFor]`, and `[ResponseDtoFor]`.

## Usage

```csharp
// Maps all entities in one call
app.MapAllCrudEndpoints();
```

## Entity as DTO

If no DTOs are provided for an entity, `MapCrudEndpoints<T>()` uses the entity itself as both the Create and Update DTO. System fields (`Id`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `TenantId`, etc.) are automatically skipped during mapping — only user-defined properties are written.

## Extending Generated Hook Stubs

The generator creates a partial hook class for each entity. Override only what you need:

```csharp
// Extend the generated partial class — only override what you need
public partial class ProductHooks
{
    public override Task BeforeCreate(Product entity, AppContext ctx)
    {
        entity.Sku = entity.Sku.ToUpperInvariant();
        return Task.CompletedTask;
    }
}
```

## Naming Templates

By default, SourceGen uses the naming convention `Create{Name}`, `Update{Name}`, `{Name}Response`, etc. Override these project-wide with an assembly-level attribute — add it to any `.cs` file (e.g. `GlobalUsings.cs`):

```csharp
[assembly: CrudKit(
    CreateDtoNamingTemplate   = "{Name}CreateRequest",   // default: "Create{Name}"
    UpdateDtoNamingTemplate   = "{Name}UpdateRequest",   // default: "Update{Name}"
    ResponseDtoNamingTemplate = "{Name}Dto",             // default: "{Name}Response"
    MapperNamingTemplate      = "{Name}Mapper",          // default: "{Name}Mapper"
    HooksNamingTemplate       = "{Name}Hooks")]          // default: "{Name}Hooks"
```

The `{Name}` placeholder is required in every template. An empty template or a template missing `{Name}` produces a compile-time error.

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

## Notes

- Generated files are in the `obj/` folder and are not committed to source control.
- You can mix source-generated and manual mappings in the same project.
- If you prefer to write DTOs manually, skip `CrudKit.SourceGen` and define them yourself — the rest of CrudKit works the same.
