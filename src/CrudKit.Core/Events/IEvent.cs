namespace CrudKit.Core.Events;

/// <summary>Domain event marker interface.</summary>
public interface IEvent
{
    string EventId { get; }
    DateTime OccurredAt { get; }
}
