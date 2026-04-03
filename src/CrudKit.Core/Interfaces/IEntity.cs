namespace CrudKit.Core.Interfaces;

/// <summary>Tüm entity'lerin implement etmesi gereken temel interface.</summary>
public interface IEntity
{
    string Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
