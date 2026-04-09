using CrudKit.Core.Events;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Events;

/// <summary>
/// Default domain event dispatcher. Resolves IDomainEventHandler&lt;T&gt; from DI and invokes them.
/// Override by registering a custom IDomainEventDispatcher.
/// </summary>
public class CrudKitEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public CrudKitEventDispatcher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
    {
        foreach (var domainEvent in events)
        {
            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handlers = _serviceProvider.GetServices(handlerType);

            foreach (var handler in handlers)
            {
                await (Task)handlerType
                    .GetMethod("HandleAsync")!
                    .Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
