namespace CrudKit.Core.Events;

/// <summary>Entity güncellendiğinde yayınlanan event.</summary>
public record EntityUpdatedEvent<T>(T Entity) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
