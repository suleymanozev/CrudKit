using CrudKit.Core.Attributes;
using CrudKit.Core.Entities;

namespace CrudKit.Sample.SourceGen.Entities;

[CrudEntity(Resource = "units", ReadOnly = true)]
public class Unit : AuditableEntity
{
    [Unique]
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}
