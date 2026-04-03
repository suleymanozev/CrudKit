namespace CrudKit.Core.Interfaces;

/// <summary>Base interface that all CrudKit entities must implement.</summary>
public interface IEntity
{
    string Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
