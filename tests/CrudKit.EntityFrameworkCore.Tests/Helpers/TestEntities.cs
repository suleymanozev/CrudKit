using CrudKit.Core.Attributes;
using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Concurrency;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>Basic entity — only IAuditableEntity, no extra interfaces.</summary>
public class PersonEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>ISoftDeletable entity.</summary>
public class SoftPersonEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

/// <summary>IMultiTenant entity.</summary>
public class TenantPersonEntity : IAuditableEntity, IMultiTenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>[Audited] entity — changes are logged to the audit trail when UseAuditTrail() is enabled.</summary>
[Audited]
public class AuditPersonEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IConcurrent entity — optimistic concurrency.</summary>
public class ConcurrentEntity : IAuditableEntity, IConcurrent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Simple entity used by DbContext configuration tests.</summary>
public class InvoiceEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Entity with [Hashed] + [SkipResponse] attributes for EfRepo tests.</summary>
public class UserEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;

    [Hashed]
    [SkipResponse]
    public string? PasswordHash { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by IncludeApplier integration tests
// ---------------------------------------------------------------------------

/// <summary>
/// Parent entity with two navigations:
///   - Children (Scope.All)     → loaded for both list and detail queries
///   - Notes   (DetailOnly)     → loaded only for detail queries
/// </summary>
[DefaultInclude("Children")]
[DefaultInclude("Notes", Scope = IncludeScope.DetailOnly)]
public class ParentEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Navigation: always included (Scope.All).</summary>
    public List<ChildEntity> Children { get; set; } = [];

    /// <summary>Navigation: included only for detail queries (DetailOnly).</summary>
    public List<NoteEntity> Notes { get; set; } = [];
}

/// <summary>Child entity — belongs to <see cref="ParentEntity"/>.</summary>
public class ChildEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ParentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ParentEntity? Parent { get; set; }
}

/// <summary>Note entity — belongs to <see cref="ParentEntity"/>.</summary>
public class NoteEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public Guid ParentId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ParentEntity? Parent { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by Unique constraint restore tests
// ---------------------------------------------------------------------------

/// <summary>ISoftDeletable entity with a [Unique] property for restore-conflict tests.</summary>
public class UniqueCodeEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }

    [Unique]
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by CascadeSoftDelete tests
// ---------------------------------------------------------------------------

/// <summary>Parent entity that cascade soft-deletes its children.</summary>
[CascadeSoftDelete(typeof(ChildItemEntity), nameof(ChildItemEntity.ParentItemId))]
public class ParentItemEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
    public List<ChildItemEntity> Children { get; set; } = new();
}

/// <summary>Child entity that participates in cascade soft-delete with <see cref="ParentItemEntity"/>.</summary>
public class ChildItemEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public Guid ParentItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by Filterable/Sortable attribute tests
// ---------------------------------------------------------------------------

/// <summary>Entity where one property is explicitly blocked from filtering.</summary>
public class PartiallyFilterableEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [NotFilterable]
    public string Secret { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Entity where all properties are blocked from filtering at class level,
/// except <see cref="Name"/> which re-enables filtering via [Filterable].
/// </summary>
[NotFilterable]
public class EntityLevelNotFilterableEntity : IAuditableEntity
{
    public Guid Id { get; set; }

    [Filterable]
    public string Name { get; set; } = string.Empty;

    public string Internal { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Entity where one property is explicitly blocked from sorting.</summary>
public class PartiallySortableEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    [NotSortable]
    public int Rank { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Entity where all properties are blocked from sorting at class level,
/// except <see cref="Name"/> which re-enables sorting via [Sortable].
/// </summary>
[NotSortable]
public class EntityLevelNotSortableEntity : IAuditableEntity
{
    public Guid Id { get; set; }

    [Sortable]
    public string Name { get; set; } = string.Empty;

    public int Rank { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by CrudIndex tests
// ---------------------------------------------------------------------------

/// <summary>Multi-tenant + soft-deletable entity with [CrudIndex] for index configuration tests.</summary>
[CrudIndex("Code", IsUnique = true)]
[CrudIndex("Category", "SubCategory")]
[CrudIndex("GlobalCode", TenantAware = false)]
public class CrudIndexEntity : IAuditableEntity, ISoftDeletable, IMultiTenant
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string SubCategory { get; set; } = string.Empty;
    public string GlobalCode { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

/// <summary>Non-tenant entity with [CrudIndex] — TenantId should NOT be prepended.</summary>
[CrudIndex("Code", IsUnique = true)]
public class CrudIndexNonTenantEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by UserTracking tests
// ---------------------------------------------------------------------------

/// <summary>
/// Basic entity with user tracking fields (CreatedById, UpdatedById).
/// </summary>
public class TrackedEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // User tracking fields — populated by TrySetUserField in BeforeSaveChanges
    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }
}

/// <summary>
/// Soft-deletable entity with user tracking fields including DeletedById.
/// </summary>
public class SoftDeleteTrackedEntity : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }

    // User tracking fields — populated by TrySetUserField in BeforeSaveChanges
    public Guid? CreatedById { get; set; }
    public Guid? UpdatedById { get; set; }
    public Guid? DeletedById { get; set; }
}
