using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Concurrency;

namespace CrudKit.Api.Tests.Helpers;

// ---- Basic entity ----
[CrudEntity]
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
[CrudEntity]
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

[CrudEntity]
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
[CrudEntity]
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
[CrudEntity]
public class InvoiceEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[CrudEntity]
public class InvoiceLineEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateInvoiceDto { [Required] public string Title { get; set; } = string.Empty; }
public class UpdateInvoiceDto { public string? Title { get; set; } }
public class CreateInvoiceLineDto
{
    public Guid InvoiceId { get; set; }
    [Required] public string Description { get; set; } = string.Empty;
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; set; }
}

// ---- Feature flag test entities ----

/// <summary>
/// Entity with no export/import attributes — relies entirely on global flags.
/// </summary>
[CrudEntity]
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
[CrudEntity]
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

// ---- Entity-level auth test entities ----

/// <summary>
/// Entity requiring authentication for all endpoints via [RequireAuth].
/// </summary>
[CrudEntity(Resource = "secured_items")]
[RequireAuth]
public class SecuredEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateSecuredDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdateSecuredDto { public string? Name { get; set; } }

/// <summary>
/// Entity requiring the "admin" role for all endpoints via [RequireRole].
/// </summary>
[CrudEntity(Resource = "admin_items")]
[RequireRole("admin")]
public class AdminEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateAdminDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdateAdminDto { public string? Name { get; set; } }

/// <summary>
/// Entity requiring convention-based permissions via [RequirePermissions].
/// </summary>
[CrudEntity(Resource = "perm_items")]
[RequirePermissions]
public class PermissionEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreatePermissionDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdatePermissionDto { public string? Name { get; set; } }

/// <summary>
/// Entity with per-operation auth via [AuthorizeOperation].
/// </summary>
[CrudEntity(Resource = "op_items")]
[AuthorizeOperation("Read", "user")]
[AuthorizeOperation("Delete", "admin")]
public class OpAuthEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateOpAuthDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdateOpAuthDto { public string? Name { get; set; } }

/// <summary>
/// Entity used for route-less mapping tests.
/// </summary>
[CrudEntity(Resource = "auto-routed")]
public class AutoRoutedEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateAutoRoutedDto { [Required] public string Name { get; set; } = string.Empty; }
public class UpdateAutoRoutedDto { public string? Name { get; set; } }

// ---- [ChildOf] auto-discovery test entities ----

/// <summary>Master entity used to test [ChildOf] auto-discovery.</summary>
[CrudEntity]
public class ProjectEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    [Required] public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateProjectDto { [Required] public string Title { get; set; } = string.Empty; }
public class UpdateProjectDto { public string? Title { get; set; } }

/// <summary>
/// Child entity using default FK and route conventions.
/// Route: "project-tasks" (ProjectTask → project-task + s)
/// FK: "ProjectEntityId" would be wrong; we use ForeignKey = "ProjectId" explicitly
/// to avoid ambiguity with the entity class name suffix.
/// </summary>
[CrudEntity]
[ChildOf(typeof(ProjectEntity), ForeignKey = "ProjectId")]
public class ProjectTaskEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    [Required] public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Child entity with a custom route and custom FK via [ChildOf].
/// Route: "milestones", FK: "ParentProjectId"
/// </summary>
[CrudEntity]
[ChildOf(typeof(ProjectEntity), Route = "milestones", ForeignKey = "ParentProjectId")]
public class ProjectMilestoneEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid ParentProjectId { get; set; }
    [Required] public string Label { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateProjectMilestoneDto
{
    public Guid ParentProjectId { get; set; }
    [Required] public string Label { get; set; } = string.Empty;
}

/// <summary>
/// Create DTO for ProjectTaskEntity — decorated with [CreateDtoFor] so auto-discovery
/// registers a POST endpoint under /api/projects/{masterId}/project-task-entitys.
/// </summary>
[CreateDtoFor(typeof(ProjectTaskEntity))]
public class CreateProjectTaskDto
{
    public Guid ProjectId { get; set; }
    [Required] public string Name { get; set; } = string.Empty;
}
