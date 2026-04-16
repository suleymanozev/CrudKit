using CrudKit.Core.Models;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Abstraction for idempotency record storage.
/// Default implementation uses EF Core; consumers can replace with Redis, etc.
/// </summary>
public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(string compoundKey, string? tenantId, CancellationToken ct = default);
    Task SaveAsync(IdempotencyRecord record, CancellationToken ct = default);
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}
