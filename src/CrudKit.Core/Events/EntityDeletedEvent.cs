namespace CrudKit.Core.Events;

/// <summary>Published when an entity is deleted (soft or hard delete).</summary>
public record EntityDeletedEvent<T>(string EntityId) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
