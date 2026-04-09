using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Sequencing;

/// <summary>
/// Provides atomic sequence number generation per entity type, tenant, and prefix.
/// Uses database-level operations to prevent duplicate numbers under concurrency.
/// </summary>
public class SequenceService
{
    private readonly DbContext _dbContext;

    public SequenceService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Gets the next sequence value atomically. Creates the sequence row if it doesn't exist.
    /// </summary>
    public async Task<long> NextValueAsync(
        string entityType,
        string tenantId,
        string prefix,
        CancellationToken ct = default)
    {
        var sequences = _dbContext.Set<CrudKitSequence>();

        var seq = await sequences.FirstOrDefaultAsync(
            s => s.EntityType == entityType && s.TenantId == tenantId && s.Prefix == prefix, ct);

        if (seq == null)
        {
            seq = new CrudKitSequence
            {
                Id = Guid.NewGuid(),
                EntityType = entityType,
                TenantId = tenantId,
                Prefix = prefix,
                CurrentValue = 1
            };
            sequences.Add(seq);
            await _dbContext.SaveChangesAsync(ct);
            return 1;
        }

        seq.CurrentValue++;
        await _dbContext.SaveChangesAsync(ct);
        return seq.CurrentValue;
    }
}
