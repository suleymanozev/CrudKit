---
sidebar_position: 8
title: Domain Events
---

# Domain Events

Aggregate roots can raise domain events that are dispatched automatically after `SaveChanges`.

## Setup

```csharp
opts.UseDomainEvents();
// or with assembly scanning:
opts.UseDomainEvents(cfg => cfg.ScanHandlersFromAssembly(typeof(Program).Assembly));
// or with custom dispatcher:
opts.UseDomainEvents<MyCustomDispatcher>();
```

## Raising Events

```csharp
public class Invoice : FullAuditableAggregateRoot
{
    public void Approve()
    {
        Status = InvoiceStatus.Approved;
        AddDomainEvent(new InvoiceApprovedEvent(Id));
    }
}
```

## Handling Events

```csharp
public record InvoiceApprovedEvent(Guid InvoiceId) : IDomainEvent;

public class CreateStockMovementHandler : IDomainEventHandler<InvoiceApprovedEvent>
{
    public async Task HandleAsync(InvoiceApprovedEvent e, CancellationToken ct = default)
    {
        // Create stock movement, send notification, etc.
    }
}
```

## Default Dispatcher

CrudKit provides `CrudKitEventDispatcher` that resolves handlers from DI. Override with `UseDomainEvents<TDispatcher>()`.

## Full Example: Invoice Approval Flow

```csharp
// 1. Define the event
public record InvoiceApprovedEvent(Guid InvoiceId, decimal Total) : IDomainEvent;

// 2. Entity raises event
[CrudEntity]
public class Invoice : FullAuditableAggregateRoot, IStateMachine<InvoiceStatus>
{
    public string InvoiceNumber { get; set; } = "";
    public decimal Total { get; set; }

    [Protected]
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public static IReadOnlyList<(InvoiceStatus, InvoiceStatus, string)> Transitions =>
    [
        (InvoiceStatus.Draft, InvoiceStatus.Approved, "approve"),
    ];
}

// 3. Hook raises the event during transition
public class InvoiceTransitionHook : ITransitionHook<Invoice>
{
    public Task BeforeTransition(Invoice entity, string action, object? payload, AppContext ctx)
    {
        if (action == "approve")
            entity.AddDomainEvent(new InvoiceApprovedEvent(entity.Id, entity.Total));
        return Task.CompletedTask;
    }
}

// 4. Handler reacts
public class SendInvoiceEmailHandler : IDomainEventHandler<InvoiceApprovedEvent>
{
    private readonly IEmailService _email;

    public SendInvoiceEmailHandler(IEmailService email) => _email = email;

    public async Task HandleAsync(InvoiceApprovedEvent e, CancellationToken ct = default)
    {
        await _email.SendAsync($"Invoice {e.InvoiceId} approved for {e.Total:C}");
    }
}

// 5. Multiple handlers for the same event
public class CreateAccountingEntryHandler : IDomainEventHandler<InvoiceApprovedEvent>
{
    public async Task HandleAsync(InvoiceApprovedEvent e, CancellationToken ct = default)
    {
        // Create accounting journal entry...
    }
}
```

## Dispatch Timing

Events are dispatched **after** `SaveChanges` succeeds, inside the same scope. If dispatch fails, the DB changes are already committed.

:::caution
Domain event handlers run in the same scope but **not** in the same transaction. If a handler fails, the original entity changes are already persisted. Design handlers to be idempotent or use compensating actions.
:::
