namespace CrudKit.Core.Events;

/// <summary>Published when an entity is updated.</summary>
public record EntityUpdatedEvent<T>(T Entity) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
