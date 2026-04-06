using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CrudKit.EntityFrameworkCore.Numbering;

/// <summary>
/// Generates sequential, tenant-scoped document numbers.
/// Format: PREFIX-YYYY-NNNNN (e.g. INV-2026-00001).
/// Uses a single atomic SQL statement per database provider — no SELECT, no retry loop,
/// no optimistic concurrency exceptions under high concurrency.
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

        var dialect = DialectDetector.Detect(_db);
        var sql = dialect.GetAtomicIncrementSql(
            "__crud_sequences", "CurrentVal",
            ["EntityType", "TenantId", "Year"]);

        // Attempt atomic increment; if no row exists, insert and return 1.
        var nextVal = await TryAtomicIncrementAsync(sql, entityType, tenantId, year, ct);

        if (nextVal == null)
        {
            // No row matched — first use for this entity+tenant+year combination.
            await InsertFirstEntryAsync(entityType, tenantId, year, ct);
            nextVal = 1L;
        }

        return $"{prefix}-{year}-{nextVal:D5}";
    }

    /// <summary>
    /// Runs the atomic UPDATE ... RETURNING (or OUTPUT) statement.
    /// Returns the new value, or null if no row matched the WHERE clause.
    /// </summary>
    private async Task<long?> TryAtomicIncrementAsync(
        string sql, string entityType, string tenantId, string year, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();

        // Open only if not already open (e.g. ambient transaction or already-open in-memory connection).
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await _db.Database.OpenConnectionAsync(ct);

        try
        {
            using var command = connection.CreateCommand();

            // Enlist in an ambient transaction if one is in progress.
            var currentTransaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (currentTransaction != null)
                command.Transaction = currentTransaction;

            command.CommandText = sql;

            var p0 = command.CreateParameter();
            p0.ParameterName = "@p0";
            p0.Value = entityType;
            command.Parameters.Add(p0);

            var p1 = command.CreateParameter();
            p1.ParameterName = "@p1";
            p1.Value = tenantId;
            command.Parameters.Add(p1);

            var p2 = command.CreateParameter();
            p2.ParameterName = "@p2";
            p2.Value = year;
            command.Parameters.Add(p2);

            var result = await command.ExecuteScalarAsync(ct);

            if (result == null || result == DBNull.Value)
                return null; // no row matched — caller must insert

            return Convert.ToInt64(result);
        }
        finally
        {
            if (shouldClose)
                await _db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Inserts the first sequence row (CurrentVal = 1) for a new entity+tenant+year combination.
    /// Silently swallows unique-constraint violations that arise when two concurrent requests
    /// race to insert the same row — the loser simply re-runs the atomic increment next call.
    /// </summary>
    private async Task InsertFirstEntryAsync(
        string entityType, string tenantId, string year, CancellationToken ct)
    {
        var entry = new SequenceEntry
        {
            EntityType = entityType,
            TenantId = tenantId,
            Year = year,
            CurrentVal = 1,
        };

        _db.Sequences.Add(entry);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another concurrent request already inserted this row — detach and proceed.
            // The caller already treats a missing row as value 1, so no retry is needed.
            foreach (var e in _db.ChangeTracker.Entries())
                e.State = EntityState.Detached;
        }
    }
}
