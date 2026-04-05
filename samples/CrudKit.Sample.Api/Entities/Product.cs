using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;

namespace CrudKit.Sample.Api.Entities;

[CrudEntity(Table = "products")]
public class Product : IAuditableEntity
{
    public Guid Id { get; set; }

    [Required, MaxLength(200), Searchable]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 999_999.99)]
    public decimal Price { get; set; }

    public string? Description { get; set; }

    [Unique]
    public string Sku { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
