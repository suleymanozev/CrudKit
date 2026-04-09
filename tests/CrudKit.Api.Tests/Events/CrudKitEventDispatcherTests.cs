using CrudKit.Api.Events;
using CrudKit.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Events;

public class CrudKitEventDispatcherTests
{
    private record OrderPlacedEvent(Guid OrderId) : IDomainEvent;
    private record OrderCancelledEvent(Guid OrderId) : IDomainEvent;

    private class OrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
    {
        public OrderPlacedEvent? Received;
        public Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken ct = default)
        {
            Received = domainEvent;
            return Task.CompletedTask;
        }
    }

    private class SecondOrderPlacedHandler : IDomainEventHandler<OrderPlacedEvent>
    {
        public bool WasCalled;
        public Task HandleAsync(OrderPlacedEvent domainEvent, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    private class OrderCancelledHandler : IDomainEventHandler<OrderCancelledEvent>
    {
        public bool WasCalled;
        public Task HandleAsync(OrderCancelledEvent domainEvent, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchAsync_CallsCorrectHandler()
    {
        var handler = new OrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        var orderId = Guid.NewGuid();
        await dispatcher.DispatchAsync([new OrderPlacedEvent(orderId)]);

        Assert.NotNull(handler.Received);
        Assert.Equal(orderId, handler.Received!.OrderId);
    }

    [Fact]
    public async Task DispatchAsync_CallsMultipleHandlersForSameEvent()
    {
        var handler1 = new OrderPlacedHandler();
        var handler2 = new SecondOrderPlacedHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler1);
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(handler2);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([new OrderPlacedEvent(Guid.NewGuid())]);

        Assert.NotNull(handler1.Received);
        Assert.True(handler2.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_DispatchesMultipleEventTypes()
    {
        var placedHandler = new OrderPlacedHandler();
        var cancelledHandler = new OrderCancelledHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<OrderPlacedEvent>>(placedHandler);
        services.AddSingleton<IDomainEventHandler<OrderCancelledEvent>>(cancelledHandler);
        var sp = services.BuildServiceProvider();

        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([
            new OrderPlacedEvent(Guid.NewGuid()),
            new OrderCancelledEvent(Guid.NewGuid())
        ]);

        Assert.NotNull(placedHandler.Received);
        Assert.True(cancelledHandler.WasCalled);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_DoesNotThrow()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([new OrderPlacedEvent(Guid.NewGuid())]);
    }

    [Fact]
    public async Task DispatchAsync_EmptyList_DoesNothing()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var dispatcher = new CrudKitEventDispatcher(sp);
        await dispatcher.DispatchAsync([]);
    }
}
