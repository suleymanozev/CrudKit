---
sidebar_position: 7
title: State Machine
---

# State Machine

Implement `IStateMachine<TState>` to add state transition endpoints. CrudKit maps `POST /{id}/transition/{action}` automatically.

## Example

```csharp
public enum OrderStatus { Pending, Processing, Completed, Cancelled }

[CrudEntity(Resource = "orders")]
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

## Notes

- The `Status` property should be marked `[Protected]` to prevent clients from bypassing the state machine via the Update endpoint.
- Enum values are stored as strings when `opts.UseEnumAsString()` is configured.
- Transition validation is strict — attempting a transition that is not listed in `Transitions` returns `400`.
