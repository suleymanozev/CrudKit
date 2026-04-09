namespace CrudKit.Core.Events;

/// <summary>
/// Dispatches collected domain events to their handlers.
/// CrudKit provides a default implementation; override via UseDomainEvents&lt;TDispatcher&gt;().
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default);
}
