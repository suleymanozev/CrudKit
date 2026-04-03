using CrudKit.Core.Interfaces;
using CrudKit.Core.Attributes;
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
