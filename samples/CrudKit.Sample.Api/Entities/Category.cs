using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;

namespace CrudKit.Sample.Api.Entities;

[CrudEntity(Table = "categories", SoftDelete = true)]
public class Category : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(100), Unique]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
