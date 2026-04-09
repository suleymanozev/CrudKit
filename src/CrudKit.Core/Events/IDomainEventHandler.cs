namespace CrudKit.Core.Events;

/// <summary>
/// Handles a specific domain event type.
/// Register implementations in DI or use assembly scanning via UseDomainEvents().
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
