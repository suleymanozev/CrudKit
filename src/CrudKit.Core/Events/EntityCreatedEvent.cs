namespace CrudKit.Core.Events;

/// <summary>Entity oluşturulduğunda yayınlanan event.</summary>
public record EntityCreatedEvent<T>(T Entity) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
