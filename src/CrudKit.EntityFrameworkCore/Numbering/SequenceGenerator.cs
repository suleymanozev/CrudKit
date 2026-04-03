using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Numbering;

/// <summary>
/// Generates sequential, tenant-scoped document numbers.
/// Format: PREFIX-YYYY-NNNNN (e.g. INV-2026-00001).
/// Uses a database transaction to ensure monotonically increasing values without gaps.
/// Thread-safe via EF Core optimistic concurrency (retry on conflict).
/// </summary>
public class SequenceGenerator
{
    private readonly CrudKitDbContext _db;

    public SequenceGenerator(CrudKitDbContext db) => _db = db;

    public async Task<string> Next<T>(string tenantId, CancellationToken ct = default)
        where T : class, IDocumentNumbering
    {
        var prefix = T.Prefix;
        var yearlyReset = T.YearlyReset;
        var entityType = typeof(T).Name;
        var year = yearlyReset ? DateTime.UtcNow.Year.ToString() : "0000";

        long nextVal;
        const int MaxRetries = 10;
        var attempts = 0;

        // Retry loop — handles concurrent requests via optimistic concurrency.
        while (true)
        {
            if (++attempts > MaxRetries)
                throw new InvalidOperationException(
                    $"Failed to generate sequence number for {entityType} after {MaxRetries} attempts.");

            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var entry = await _db.Sequences
                    .FirstOrDefaultAsync(
                        s => s.EntityType == entityType
                          && s.TenantId == tenantId
                          && s.Year == year,
                        ct);

                if (entry == null)
                {
                    entry = new SequenceEntry
                    {
                        EntityType = entityType,
                        TenantId = tenantId,
                        Year = year,
                        CurrentVal = 1,
                    };
                    _db.Sequences.Add(entry);
                    nextVal = 1;
                }
                else
                {
                    entry.CurrentVal++;
                    nextVal = entry.CurrentVal;
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                break;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Transaction auto-rolls back on dispose; detach stale entries and retry.
                foreach (var efEntry in _db.ChangeTracker.Entries())
                    efEntry.State = EntityState.Detached;
            }
        }

        return $"{prefix}-{year}-{nextVal:D5}";
    }
}
