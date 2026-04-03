namespace CrudKit.Core.Events;

/// <summary>Entity silindiğinde (soft veya hard delete) yayınlanan event.</summary>
public record EntityDeletedEvent<T>(string EntityId) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
