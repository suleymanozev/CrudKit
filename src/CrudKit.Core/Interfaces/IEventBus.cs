using CrudKit.Core.Events;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Abstraction for publishing domain events.
/// The application provides its own implementation (MediatR, MassTransit, etc.).
/// CrudKit's EfRepo publishes events after SaveChanges completes.
/// </summary>
public interface IEventBus
{
    Task Publish<T>(T @event, CancellationToken ct = default) where T : class, IEvent;
    void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent;
}
