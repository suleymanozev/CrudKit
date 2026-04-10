using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Entities;

namespace CrudKit.Sample.SourceGen.Entities;

[CrudEntity(Resource = "products")]
[Audited]
[Exportable]
[RequirePermissions]
public class Product : AuditableEntity
{
    [Required, MaxLength(200), Searchable]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 999_999.99)]
    public decimal Price { get; set; }

    public string? Description { get; set; }

    [Unique, SkipUpdate]
    public string Sku { get; set; } = string.Empty;

    [SkipResponse]
    public string? InternalNotes { get; set; }
}
