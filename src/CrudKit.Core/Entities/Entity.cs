using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Root base class for all entities with a typed primary key.
/// Provides the <see cref="Id"/> property and implements <see cref="IEntity{TKey}"/>.
/// </summary>
public abstract class Entity<TKey> : IEntity<TKey> where TKey : notnull
{
    public TKey Id { get; set; } = default!;
}

/// <summary>
/// Root base class for entities using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class Entity : Entity<Guid>, IEntity { }
