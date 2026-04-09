using System.Net;
using System.Net.Http.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Entities;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AppContext = CrudKit.Core.Context.AppContext;

namespace CrudKit.Api.Tests.Events;

// Test entities for domain events
public class AggregateOrderEntity : AuditableAggregateRoot
{
    public string Customer { get; set; } = string.Empty;
}

public record OrderCreatedDomainEvent(Guid OrderId) : IDomainEvent;

public class CreateAggregateOrderDto
{
    public string Customer { get; set; } = string.Empty;
}

public class UpdateAggregateOrderDto
{
    public string? Customer { get; set; }
}

// Hook that raises domain event
public class AggregateOrderHook : ICrudHooks<AggregateOrderEntity>
{
    public Task BeforeCreate(AggregateOrderEntity entity, AppContext ctx)
    {
        entity.AddDomainEvent(new OrderCreatedDomainEvent(entity.Id));
        return Task.CompletedTask;
    }
}

// Handler
public class OrderCreatedDomainHandler : IDomainEventHandler<OrderCreatedDomainEvent>
{
    public List<Guid> ReceivedIds { get; } = [];

    public Task HandleAsync(OrderCreatedDomainEvent domainEvent, CancellationToken ct = default)
    {
        ReceivedIds.Add(domainEvent.OrderId);
        return Task.CompletedTask;
    }
}

public class DomainEventDispatchIntegrationTests
{
    [Fact]
    public async Task DomainEvents_DispatchedAfterSaveChanges()
    {
        var handler = new OrderCreatedDomainHandler();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
            {
                svc.AddScoped<ICrudHooks<AggregateOrderEntity>, AggregateOrderHook>();
                svc.AddSingleton<IDomainEventHandler<OrderCreatedDomainEvent>>(handler);
            },
            configureEndpoints: web =>
                web.MapCrudEndpoints<AggregateOrderEntity, CreateAggregateOrderDto, UpdateAggregateOrderDto>("aggregate-orders"),
            configureOptions: opts => opts.UseDomainEvents());

        var response = await app.Client.PostAsJsonAsync("/api/aggregate-orders",
            new { Customer = "Test Corp" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Single(handler.ReceivedIds);
    }

    [Fact]
    public async Task DomainEvents_ClearedAfterDispatch()
    {
        var handler = new OrderCreatedDomainHandler();

        await using var app = await TestWebApp.CreateAsync(
            configureServices: svc =>
            {
                svc.AddScoped<ICrudHooks<AggregateOrderEntity>, AggregateOrderHook>();
                svc.AddSingleton<IDomainEventHandler<OrderCreatedDomainEvent>>(handler);
            },
            configureEndpoints: web =>
                web.MapCrudEndpoints<AggregateOrderEntity, CreateAggregateOrderDto, UpdateAggregateOrderDto>("aggregate-orders"),
            configureOptions: opts => opts.UseDomainEvents());

        await app.Client.PostAsJsonAsync("/api/aggregate-orders", new { Customer = "A" });
        await app.Client.PostAsJsonAsync("/api/aggregate-orders", new { Customer = "B" });

        Assert.Equal(2, handler.ReceivedIds.Count);
    }
}
