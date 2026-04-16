using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Idempotency;

/// <summary>
/// EF Core implementation of <see cref="IIdempotencyStore"/>.
/// Stores idempotency records in the __crud_idempotency table.
/// </summary>
public class EfIdempotencyStore : IIdempotencyStore
{
    private readonly CrudKitDbContext _db;

    public EfIdempotencyStore(CrudKitDbContext db) => _db = db;

    public async Task<IdempotencyRecord?> FindAsync(string compoundKey, string? tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(r =>
                r.Key == compoundKey &&
                r.TenantId == tenantId &&
                r.ExpiresAt > now, ct);
    }

    public async Task SaveAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        _db.Set<IdempotencyRecord>().Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _db.Set<IdempotencyRecord>()
            .Where(r => r.ExpiresAt <= now)
            .ExecuteDeleteAsync(ct);
    }
}
