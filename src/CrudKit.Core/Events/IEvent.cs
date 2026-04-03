namespace CrudKit.Core.Events;

/// <summary>Marker interface for domain events.</summary>
public interface IEvent
{
    string EventId { get; }
    DateTime OccurredAt { get; }
}
