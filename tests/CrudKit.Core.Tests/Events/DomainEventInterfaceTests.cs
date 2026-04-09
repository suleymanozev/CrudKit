using CrudKit.Core.Events;
using Xunit;

namespace CrudKit.Core.Tests.Events;

public class DomainEventInterfaceTests
{
    private record TestEvent(string Message) : IDomainEvent;

    private class TestHandler : IDomainEventHandler<TestEvent>
    {
        public TestEvent? Received;
        public Task HandleAsync(TestEvent domainEvent, CancellationToken ct = default)
        {
            Received = domainEvent;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void IDomainEvent_IsMarkerInterface()
    {
        var members = typeof(IDomainEvent).GetMembers()
            .Where(m => m.DeclaringType == typeof(IDomainEvent));
        Assert.Empty(members);
    }

    [Fact]
    public void IHasDomainEvents_ExposesRequiredMembers()
    {
        var type = typeof(IHasDomainEvents);
        Assert.NotNull(type.GetMethod("AddDomainEvent"));
        Assert.NotNull(type.GetMethod("ClearDomainEvents"));
        Assert.NotNull(type.GetProperty("DomainEvents"));
    }

    [Fact]
    public void IDomainEventHandler_HasHandleAsyncMethod()
    {
        var method = typeof(IDomainEventHandler<TestEvent>).GetMethod("HandleAsync");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void IDomainEventDispatcher_HasDispatchAsyncMethod()
    {
        var method = typeof(IDomainEventDispatcher).GetMethod("DispatchAsync");
        Assert.NotNull(method);
    }

    [Fact]
    public async Task TestHandler_ReceivesEvent()
    {
        var handler = new TestHandler();
        var ev = new TestEvent("hello");
        await handler.HandleAsync(ev);
        Assert.Equal("hello", handler.Received?.Message);
    }
}
