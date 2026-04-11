using System.Text.Json.Serialization;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Base class for aggregate roots with domain event support.
/// Domain events are collected and dispatched automatically during SaveChanges.
/// </summary>
public abstract class AggregateRoot<TKey> : Entity<TKey>, IHasDomainEvents
    where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [JsonIgnore]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Aggregate root using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class AggregateRoot : AggregateRoot<Guid>, IEntity { }
