namespace CrudKit.Core.Interfaces;

/// <summary>
/// Base interface for entities with a typed primary key.
/// Contains only the identifier; timestamps live in <see cref="IAuditableEntity{TKey}"/>.
/// </summary>
public interface IEntity<TKey> where TKey : notnull
{
    TKey Id { get; set; }
}

/// <summary>
/// Base interface for entities using the default <see cref="Guid"/> primary key.
/// </summary>
public interface IEntity : IEntity<Guid> { }
