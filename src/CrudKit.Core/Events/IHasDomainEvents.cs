namespace CrudKit.Core.Events;

/// <summary>
/// Implemented by entities (typically aggregate roots) that collect domain events.
/// Events are dispatched automatically during SaveChanges when a dispatcher is registered.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void AddDomainEvent(IDomainEvent domainEvent);
    void ClearDomainEvents();
}
