using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Entities;

namespace CrudKit.Sample.Api.Entities;

[CrudEntity(Resource = "categories")]
[Audited]
[RequireAuth]
public class Category : FullAuditableEntity
{
    [Required, MaxLength(100), Unique]
    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
