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

## Dispatch Timing

Events are dispatched **after** `SaveChanges` succeeds, inside the same scope. If dispatch fails, the DB changes are already committed.
