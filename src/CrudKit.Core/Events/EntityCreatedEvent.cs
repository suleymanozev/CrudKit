namespace CrudKit.Core.Events;

/// <summary>Published when an entity is created.</summary>
public record EntityCreatedEvent<T>(T Entity) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
