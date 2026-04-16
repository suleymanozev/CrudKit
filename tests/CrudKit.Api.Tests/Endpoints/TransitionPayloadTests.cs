using System.Net;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Interfaces;

using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class TransitionPayloadTests
{
    [Fact]
    public async Task Transition_WithoutPayload_Succeeds()
    {
        // "start" action has no payload requirement
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<TicketEntity, CreateTicketDto, UpdateTicketDto>("tickets");
        });

        var create = await app.Client.PostAsJsonAsync("/api/tickets", new { Title = "Bug" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var response = await app.Client.PostAsync($"/api/tickets/{id}/transition/start", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = doc.RootElement.GetProperty("status");
        if (status.ValueKind == JsonValueKind.String)
            Assert.Equal("InProgress", status.GetString());
        else
            Assert.Equal((int)TicketStatus.InProgress, status.GetInt32());
    }

    [Fact]
    public async Task Transition_WithRequiredPayload_Succeeds()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<TicketEntity, CreateTicketDto, UpdateTicketDto>("tickets");
        });

        var create = await app.Client.PostAsJsonAsync("/api/tickets", new { Title = "Bug" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // start → InProgress
        await app.Client.PostAsync($"/api/tickets/{id}/transition/start", null);

        // resolve → Resolved (requires ResolvePayload)
        var response = await app.Client.PostAsJsonAsync(
            $"/api/tickets/{id}/transition/resolve",
            new { Resolution = "Fixed in v2" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Transition_MissingRequiredPayload_Returns400()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<TicketEntity, CreateTicketDto, UpdateTicketDto>("tickets");
        });

        var create = await app.Client.PostAsJsonAsync("/api/tickets", new { Title = "Bug" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        await app.Client.PostAsync($"/api/tickets/{id}/transition/start", null);

        // resolve without payload
        var response = await app.Client.PostAsync($"/api/tickets/{id}/transition/resolve", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Transition_Hook_ReceivesPayload()
    {
        object? capturedPayload = null;
        var hook = new TestTransitionHook
        {
            OnBefore = (_, _, payload, _) => { capturedPayload = payload; return Task.CompletedTask; }
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ITransitionHook<TicketEntity>>(hook),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<TicketEntity, CreateTicketDto, UpdateTicketDto>("tickets");
            });

        var create = await app.Client.PostAsJsonAsync("/api/tickets", new { Title = "Bug" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        await app.Client.PostAsync($"/api/tickets/{id}/transition/start", null);

        await app.Client.PostAsJsonAsync(
            $"/api/tickets/{id}/transition/resolve",
            new { Resolution = "Fixed" });

        Assert.NotNull(capturedPayload);
        Assert.IsType<ResolvePayload>(capturedPayload);
        Assert.Equal("Fixed", ((ResolvePayload)capturedPayload).Resolution);
    }

    [Fact]
    public async Task Transition_Hook_WithoutPayload_ReceivesNull()
    {
        object? capturedPayload = "not-null-sentinel";
        var hook = new TestTransitionHook
        {
            OnBefore = (_, _, payload, _) => { capturedPayload = payload; return Task.CompletedTask; }
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ITransitionHook<TicketEntity>>(hook),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<TicketEntity, CreateTicketDto, UpdateTicketDto>("tickets");
            });

        var create = await app.Client.PostAsJsonAsync("/api/tickets", new { Title = "Bug" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // "start" has no payload
        await app.Client.PostAsync($"/api/tickets/{id}/transition/start", null);

        Assert.Null(capturedPayload);
    }

    [Fact]
    public async Task Transition_Hook_BeforeThrows_Aborts()
    {
        var hook = new TestTransitionHook
        {
            OnBefore = (_, _, _, _) => throw new InvalidOperationException("Blocked by hook")
        };

        await using var app = await TestWebApp.CreateAsync(
            configureServices: s => s.AddSingleton<ITransitionHook<TicketEntity>>(hook),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<TicketEntity, CreateTicketDto, UpdateTicketDto>("tickets");
            });

        var create = await app.Client.PostAsJsonAsync("/api/tickets", new { Title = "Bug" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var response = await app.Client.PostAsync($"/api/tickets/{id}/transition/start", null);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Transition_OriginalStateMachine_StillWorks()
    {
        // OrderEntity uses IStateMachine (not WithPayload) — must still work
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders");
        });

        var create = await app.Client.PostAsJsonAsync("/api/orders", new { Customer = "Alice" });
        var id = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var response = await app.Client.PostAsync($"/api/orders/{id}/transition/process", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>Delegate-based ITransitionHook for testing.</summary>
public class TestTransitionHook : ITransitionHook<TicketEntity>
{
    public Func<TicketEntity, string, object?, CrudKit.Core.Context.AppContext, Task>? OnBefore { get; set; }
    public Func<TicketEntity, string, object?, CrudKit.Core.Context.AppContext, Task>? OnAfter { get; set; }

    public Task BeforeTransition(TicketEntity entity, string action, object? payload, CrudKit.Core.Context.AppContext ctx)
        => OnBefore?.Invoke(entity, action, payload, ctx) ?? Task.CompletedTask;

    public Task AfterTransition(TicketEntity entity, string action, object? payload, CrudKit.Core.Context.AppContext ctx)
        => OnAfter?.Invoke(entity, action, payload, ctx) ?? Task.CompletedTask;
}
