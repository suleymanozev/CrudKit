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

/// <summary>IDocumentNumbering entity — auto document number generation.</summary>
public class InvoiceEntity : IAuditableEntity, IDocumentNumbering
{
    public Guid Id { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string Prefix => "INV";
    public static bool YearlyReset => true;
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
    public List<ChildItemEntity> Children { get; set; } = new();
}

/// <summary>Child entity that participates in cascade soft-delete with <see cref="ParentItemEntity"/>.</summary>
public class ChildItemEntity : IAuditableEntity, ISoftDeletable, ICascadeSoftDelete<ParentItemEntity>
{
    public Guid Id { get; set; }
    public Guid ParentItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public static string ParentForeignKey => nameof(ParentItemId);
}
