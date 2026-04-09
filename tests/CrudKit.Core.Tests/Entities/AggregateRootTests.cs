using CrudKit.Core.Entities;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Entities;

public class AggregateRootTests
{
    private record OrderCreatedEvent(Guid OrderId) : IDomainEvent;
    private record OrderShippedEvent(Guid OrderId, DateTime ShippedAt) : IDomainEvent;

    private class TestAggregate : AggregateRoot
    {
        public string Name { get; set; } = "";
        public void Ship() => AddDomainEvent(new OrderShippedEvent(Id, DateTime.UtcNow));
    }

    private class TestAuditableAggregate : AuditableAggregateRoot
    {
        public string Title { get; set; } = "";
    }

    private class TestFullAuditableAggregate : FullAuditableAggregateRoot
    {
        public string Code { get; set; } = "";
    }

    [Fact]
    public void AggregateRoot_ImplementsIHasDomainEvents()
        => Assert.True(typeof(IHasDomainEvents).IsAssignableFrom(typeof(AggregateRoot)));

    [Fact]
    public void AggregateRoot_InheritsFromEntity()
        => Assert.True(typeof(Entity<Guid>).IsAssignableFrom(typeof(AggregateRoot)));

    [Fact]
    public void AggregateRoot_StartsWithNoDomainEvents()
        => Assert.Empty(new TestAggregate().DomainEvents);

    [Fact]
    public void AddDomainEvent_AddsToCollection()
    {
        var agg = new TestAggregate { Id = Guid.NewGuid() };
        agg.Ship();
        Assert.Single(agg.DomainEvents);
        Assert.IsType<OrderShippedEvent>(agg.DomainEvents[0]);
    }

    [Fact]
    public void ClearDomainEvents_RemovesAll()
    {
        var agg = new TestAggregate { Id = Guid.NewGuid() };
        agg.Ship();
        agg.Ship();
        Assert.Equal(2, agg.DomainEvents.Count);
        agg.ClearDomainEvents();
        Assert.Empty(agg.DomainEvents);
    }

    [Fact]
    public void DomainEvents_IsReadOnly()
    {
        var agg = new TestAggregate();
        Assert.IsAssignableFrom<IReadOnlyList<IDomainEvent>>(agg.DomainEvents);
    }

    [Fact]
    public void AuditableAggregateRoot_InheritsFromAuditableEntity()
        => Assert.True(typeof(IAuditableEntity).IsAssignableFrom(typeof(AuditableAggregateRoot)));

    [Fact]
    public void AuditableAggregateRoot_ImplementsIHasDomainEvents()
        => Assert.True(typeof(IHasDomainEvents).IsAssignableFrom(typeof(AuditableAggregateRoot)));

    [Fact]
    public void AuditableAggregateRoot_HasAuditFields()
    {
        var agg = new TestAuditableAggregate();
        agg.CreatedAt = DateTime.UtcNow;
        agg.UpdatedAt = DateTime.UtcNow;
        Assert.NotEqual(default, agg.CreatedAt);
    }

    [Fact]
    public void FullAuditableAggregateRoot_ImplementsISoftDeletable()
        => Assert.True(typeof(ISoftDeletable).IsAssignableFrom(typeof(FullAuditableAggregateRoot)));

    [Fact]
    public void FullAuditableAggregateRoot_ImplementsIHasDomainEvents()
        => Assert.True(typeof(IHasDomainEvents).IsAssignableFrom(typeof(FullAuditableAggregateRoot)));

    [Fact]
    public void FullAuditableAggregateRoot_CanAddDomainEvents()
    {
        var agg = new TestFullAuditableAggregate { Id = Guid.NewGuid() };
        agg.AddDomainEvent(new OrderCreatedEvent(agg.Id));
        Assert.Single(agg.DomainEvents);
    }
}
