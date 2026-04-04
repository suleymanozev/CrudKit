using CrudKit.Core.Interfaces;
using CrudKit.Core.Attributes;
using CrudKit.Core.Enums;
using CrudKit.EntityFrameworkCore.Concurrency;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>Basic entity — only IEntity, no extra interfaces.</summary>
public class PersonEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>ISoftDeletable entity.</summary>
public class SoftPersonEntity : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

/// <summary>IMultiTenant entity.</summary>
public class TenantPersonEntity : IEntity, IMultiTenant
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IAuditable entity.</summary>
public class AuditPersonEntity : IEntity, IAuditable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IConcurrent entity — optimistic concurrency.</summary>
public class ConcurrentEntity : IEntity, IConcurrent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>IDocumentNumbering entity — auto document number generation.</summary>
public class InvoiceEntity : IEntity, IDocumentNumbering
{
    public string Id { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static string Prefix => "INV";
    public static bool YearlyReset => true;
}

/// <summary>Entity with [Hashed] + [SkipResponse] attributes for EfRepo tests.</summary>
public class UserEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
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
public class ParentEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Navigation: always included (Scope.All).</summary>
    public List<ChildEntity> Children { get; set; } = [];

    /// <summary>Navigation: included only for detail queries (DetailOnly).</summary>
    public List<NoteEntity> Notes { get; set; } = [];
}

/// <summary>Child entity — belongs to <see cref="ParentEntity"/>.</summary>
public class ChildEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ParentEntity? Parent { get; set; }
}

/// <summary>Note entity — belongs to <see cref="ParentEntity"/>.</summary>
public class NoteEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ParentEntity? Parent { get; set; }
}

// ---------------------------------------------------------------------------
// Entities used by CascadeSoftDelete tests
// ---------------------------------------------------------------------------

/// <summary>Parent entity that cascade soft-deletes its children.</summary>
[CascadeSoftDelete(typeof(ChildItemEntity), nameof(ChildItemEntity.ParentItemId))]
public class ParentItemEntity : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<ChildItemEntity> Children { get; set; } = new();
}

/// <summary>Child entity that participates in cascade soft-delete with <see cref="ParentItemEntity"/>.</summary>
public class ChildItemEntity : IEntity, ISoftDeletable, ICascadeSoftDelete<ParentItemEntity>
{
    public string Id { get; set; } = string.Empty;
    public string ParentItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public static string ParentForeignKey => nameof(ParentItemId);
}
