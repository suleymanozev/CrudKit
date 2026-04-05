using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;

namespace CrudKit.Sample.Api.Entities;

/// <summary>
/// Read-only lookup entity (e.g., kg, piece, liter).
/// No create/update/delete endpoints — managed via seed data or migration.
/// </summary>
[CrudEntity(Table = "units", ReadOnly = true)]
public class Unit : IAuditableEntity
{
    public Guid Id { get; set; }

    [Unique]
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
