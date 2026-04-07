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

| Generated File | Description |
|----------------|-------------|
| `{Entity}CreateDto.g.cs` | Create DTO record |
| `{Entity}UpdateDto.g.cs` | Update DTO with `Optional<T>` fields |
| `{Entity}ResponseDto.g.cs` | Response DTO |
| `{Entity}Mapper.g.cs` | `ICrudMapper` implementation |
| `{Entity}Hooks.g.cs` | Partial hook stub to extend |
| `CrudKitEndpoints.g.cs` | `MapAllCrudEndpoints()` extension method |
| `CrudKitMappers.g.cs` | DI registration for all mappers (`AddAllCrudMappers()`) |

## Usage

```csharp
// Maps all entities in one call
app.MapAllCrudEndpoints();

// Registers all generated mappers
builder.Services.AddAllCrudMappers();
```

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

## Manual DTOs — [CreateDtoFor] / [UpdateDtoFor]

When you want full control over the shape of a create or update DTO, annotate your hand-written record or class with `[CreateDtoFor(typeof(TEntity))]` or `[UpdateDtoFor(typeof(TEntity))]`. SourceGen detects these attributes and skips generating that DTO for the entity. The `ResponseDto` and mapper are still generated.

```csharp
[CreateDtoFor(typeof(Order))]
public record CreateOrder([Required] string CustomerName, decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
}
// ResponseDto and Mapper for Order are still auto-generated.
```

## Notes

- Generated files are in the `obj/` folder and are not committed to source control.
- You can mix source-generated and manual mappings in the same project.
- If you prefer to write DTOs manually, skip `CrudKit.SourceGen` and define them yourself — the rest of CrudKit works the same.
