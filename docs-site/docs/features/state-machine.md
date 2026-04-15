---
sidebar_position: 7
title: State Machine
---

# State Machine

Implement `IStateMachine<TState>` to add state transition endpoints. CrudKit maps `POST /{id}/transition/{action}` automatically.

## Example

```csharp
public enum OrderStatus { Pending, Processing, Completed, Cancelled }

[CrudEntity]
public class Order : FullAuditableEntity, IStateMachine<OrderStatus>
{
    public string CustomerName { get; set; } = string.Empty;
    public decimal Total { get; set; }

    [Protected]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}
```

`POST /api/orders/{id}/transition/process` — moves from `Pending` to `Processing`.

Invalid transitions return `400 BAD_REQUEST`. Use `[Protected]` on the `Status` field to prevent it from being set directly via the Update DTO.

## IStateMachine\<TState\> Interface

```csharp
public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}
```

## Generated Endpoint

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/{entity}/{id}/transition/{action}` | Execute a named transition |

The endpoint is only generated when the entity implements `IStateMachine<TState>`.

## Typed Transition Payloads

Some transitions require additional data (e.g., a rejection reason). Use `IStateMachineWithPayload<TState>` to define typed payloads per action:

```csharp
public record RejectPayload(string Reason);
public record CancelPayload(string Reason, bool RefundRequested);

[CrudEntity]
public class Invoice : FullAuditableEntity, IStateMachineWithPayload<InvoiceStatus>
{
    [Protected]
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public static IReadOnlyList<(InvoiceStatus From, InvoiceStatus To, string Action)> Transitions =>
    [
        (InvoiceStatus.Draft,    InvoiceStatus.Approved,  "approve"),
        (InvoiceStatus.Draft,    InvoiceStatus.Rejected,  "reject"),
        (InvoiceStatus.Approved, InvoiceStatus.Cancelled, "cancel"),
    ];

    // Only actions that require a payload — "approve" needs no payload
    public static IReadOnlyDictionary<string, Type> TransitionPayloads => new Dictionary<string, Type>
    {
        ["reject"] = typeof(RejectPayload),
        ["cancel"] = typeof(CancelPayload),
    };
}
```

```
POST /api/invoices/{id}/transition/approve    → no body needed
POST /api/invoices/{id}/transition/reject     → { "reason": "Missing documents" }
POST /api/invoices/{id}/transition/cancel     → { "reason": "Customer request", "refundRequested": true }
```

If a payload is required but not provided, the endpoint returns `400 Bad Request`.

## Transition Hooks

Use `ITransitionHook<T>` to run logic before or after a transition. The typed payload is passed to the hook:

```csharp
public class InvoiceTransitionHook : ITransitionHook<Invoice>
{
    private readonly INotificationService _notifications;

    public InvoiceTransitionHook(INotificationService notifications)
        => _notifications = notifications;

    public async Task BeforeTransition(Invoice entity, string action, object? payload, AppContext ctx)
    {
        if (action == "reject" && payload is RejectPayload reject)
        {
            if (string.IsNullOrWhiteSpace(reject.Reason))
                throw new InvalidOperationException("Rejection reason is required.");

            // Persist payload data on the entity — CrudKit does not store payloads automatically
            entity.RejectionReason = reject.Reason;
        }
    }

    public async Task AfterTransition(Invoice entity, string action, object? payload, AppContext ctx)
    {
        await _notifications.SendAsync($"Invoice {entity.Id} transitioned via '{action}'");
    }
}

// Register in DI
builder.Services.AddScoped<ITransitionHook<Invoice>, InvoiceTransitionHook>();
```

:::info Payload Persistence
CrudKit deserializes the payload and passes it to hooks, but **does not persist it automatically**. To save payload data, write it to the entity in `BeforeTransition` (as shown above) — the entity is tracked and saved within the same transaction.
:::

## Notes

- The `Status` property should be marked `[Protected]` to prevent clients from bypassing the state machine via the Update endpoint.
- Enum values are stored as strings when `opts.UseEnumAsString()` is configured.
- Transition validation is strict — attempting a transition that is not listed in `Transitions` returns `400`.
- `IStateMachineWithPayload<TState>` extends `IStateMachine<TState>` — existing entities without payloads continue to work unchanged.
