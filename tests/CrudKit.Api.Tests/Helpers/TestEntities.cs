using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Concurrency;

namespace CrudKit.Api.Tests.Helpers;

// ---- Basic entity ----
[Exportable]
[Importable]
public class ProductEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    [NotExportable]
    public string? InternalNotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateProductDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Range(0.01, 1_000_000)] public decimal Price { get; set; }
}

public class UpdateProductDto
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
}

public record ProductResponse(Guid Id, string Name, decimal Price, string DisplayName);

// ---- Soft-deletable ----
public class SoftProductEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class CreateSoftProductDto
{
    [Required] public string Name { get; set; } = string.Empty;
}

// ---- State machine ----
public enum OrderStatus { Pending, Processing, Completed, Cancelled }

public class OrderEntity : IAuditableEntity, IStateMachine<OrderStatus>
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}

public class CreateOrderDto
{
    [Required] public string Customer { get; set; } = string.Empty;
}

public class UpdateOrderDto
{
    public string? Customer { get; set; }
}

// ---- Concurrent ----
public class ConcurrentEntity : IAuditableEntity, IConcurrent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateConcurrentDto
{
    [Required] public string Name { get; set; } = string.Empty;
}

public class UpdateConcurrentDto
{
    public string? Name { get; set; }
    public uint RowVersion { get; set; }
}

// ---- Master-detail ----
public class InvoiceEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class InvoiceLineEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string InvoiceId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateInvoiceDto { [Required] public string Title { get; set; } = string.Empty; }
public class UpdateInvoiceDto { public string? Title { get; set; } }
public class CreateInvoiceLineDto
{
    public string InvoiceId { get; set; } = string.Empty;
    [Required] public string Description { get; set; } = string.Empty;
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; set; }
}

// ---- Feature flag test entities ----

/// <summary>
/// Entity with no export/import attributes — relies entirely on global flags.
/// </summary>
public class NoFlagEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateNoFlagDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdateNoFlagDto { public string? Name { get; set; } }

/// <summary>
/// Entity explicitly opting out of export and import via class-level [NotExportable] / [NotImportable].
/// Even with global UseExport()/UseImport(), these endpoints must not exist.
/// </summary>
[NotExportable]
[NotImportable]
public class OptOutEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateOptOutDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdateOptOutDto { public string? Name { get; set; } }
