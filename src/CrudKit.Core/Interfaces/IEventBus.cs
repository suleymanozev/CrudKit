using CrudKit.Core.Events;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Domain event yayını için soyutlama.
/// Kullanıcı kendi IEventBus implementasyonunu sağlar (MediatR, MassTransit, vb.).
/// CrudKit EfRepo, SaveChanges sonrasında event yayınlar.
/// </summary>
public interface IEventBus
{
    Task Publish<T>(T @event, CancellationToken ct = default) where T : class, IEvent;
    void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent;
}
