using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Aggregate root with audit fields, soft delete, and domain event support.
/// </summary>
public abstract class FullAuditableAggregateRoot<TKey> : AuditableEntity<TKey>, ISoftDeletable, IHasDomainEvents
    where TKey : notnull
{
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Full auditable aggregate root using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class FullAuditableAggregateRoot : FullAuditableAggregateRoot<Guid>, IEntity, IAuditableEntity { }

/// <summary>
/// Full auditable aggregate root with user tracking (CreatedBy, UpdatedBy, DeletedBy) and domain events.
/// </summary>
public abstract class FullAuditableAggregateRootWithUser<TKey, TUser, TUserKey>
    : AuditableEntityWithUser<TKey, TUser, TUserKey>, ISoftDeletable, IHasDomainEvents
    where TKey : notnull
    where TUser : class
    where TUserKey : notnull
{
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
    public TUserKey? DeletedById { get; set; }
    public TUser? DeletedBy { get; set; }

    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

/// <summary>
/// Full auditable aggregate root with user tracking using default Guid keys.
/// </summary>
public abstract class FullAuditableAggregateRootWithUser<TUser>
    : FullAuditableAggregateRootWithUser<Guid, TUser, Guid>
    where TUser : class { }
