using System.Text.Json.Serialization;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Aggregate root with audit fields (CreatedAt, UpdatedAt) and domain event support.
/// </summary>
public abstract class AuditableAggregateRoot<TKey> : AuditableEntity<TKey>, IHasDomainEvents
    where TKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [JsonIgnore]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Auditable aggregate root using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class AuditableAggregateRoot : AuditableAggregateRoot<Guid>, IEntity, IAuditableEntity { }

/// <summary>
/// Auditable aggregate root with user tracking (CreatedBy, UpdatedBy) and domain events.
/// </summary>
public abstract class AuditableAggregateRootWithUser<TKey, TUser, TUserKey>
    : AuditableEntityWithUser<TKey, TUser, TUserKey>, IHasDomainEvents
    where TKey : notnull
    where TUser : class
    where TUserKey : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [JsonIgnore]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Auditable aggregate root with user tracking using default Guid keys.
/// </summary>
public abstract class AuditableAggregateRootWithUser<TUser>
    : AuditableAggregateRootWithUser<Guid, TUser, Guid>
    where TUser : class { }
