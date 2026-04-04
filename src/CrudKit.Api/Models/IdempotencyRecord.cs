namespace CrudKit.Api.Models;

/// <summary>
/// Stores a cached response for an idempotency key so that repeated requests
/// with the same key return the original result without re-executing the handler.
/// Consumers must register this entity in their DbContext (e.g. via OnModelCreatingCustom).
/// </summary>
public class IdempotencyRecord
{
    /// <summary>Primary key (GUID string).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Compound key: {userId}:{idempotency-header-value}.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Request path (e.g. /api/orders).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP method (POST, PUT, PATCH, DELETE).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>HTTP status code of the original response.</summary>
    public int StatusCode { get; set; }

    /// <summary>Serialised JSON body of the original response.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>User who made the original request (nullable for anonymous).</summary>
    public string? UserId { get; set; }

    /// <summary>Tenant that owns this record (nullable for non-multi-tenant apps).</summary>
    public string? TenantId { get; set; }

    /// <summary>When this record was first created (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When this record becomes invalid and eligible for cleanup (UTC).</summary>
    public DateTime ExpiresAt { get; set; }
}
