using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Tenancy;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Auditing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Factory for creating isolated SQLite in-memory test database instances.
/// Each call produces a fresh database. The TestDbContext takes ownership
/// of the SqliteConnection and disposes it when the context is disposed.
/// </summary>
public static class DbHelper
{
    public static TestDbContext CreateDb(ICurrentUser? user = null, TimeProvider? timeProvider = null,
        bool auditTrailEnabled = false, bool enumAsStringEnabled = false,
        ITenantContext? tenantContext = null, IAuditWriter? auditWriter = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .Options;

        CrudKitEfOptions? efOptions = null;
        if (auditTrailEnabled || enumAsStringEnabled)
        {
            efOptions = new CrudKitEfOptions
            {
                AuditTrailEnabled = auditTrailEnabled,
                EnumAsStringEnabled = enumAsStringEnabled,
            };
        }

        // When audit trail is enabled and no custom writer is provided, use a
        // TestDbAuditWriter that defers the context reference until first write.
        IAuditWriter? writer = auditWriter;
        TestDbAuditWriter? testWriter = null;
        if (auditTrailEnabled && writer == null)
        {
            testWriter = new TestDbAuditWriter();
            writer = testWriter;
        }

        var db = new TestDbContext(options, user ?? new FakeCurrentUser(), connection, timeProvider, efOptions, tenantContext, writer);
        db.Database.EnsureCreated();

        // Now bind the writer to the context it was injected into.
        testWriter?.Bind(db);

        return db;
    }

    /// <summary>
    /// Build a minimal IServiceProvider that resolves the given CrudKitDbContext.
    /// Used by tests that construct EfRepo manually.
    /// </summary>
    public static IServiceProvider WrapAsServiceProvider(CrudKitDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<CrudKitDbContext>(db);
        services.AddSingleton<ICrudKitDbContext>(db);
        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Test helper that writes audit entries to the same context's AuditLogs DbSet.
/// Allows the context reference to be set after construction, solving the
/// chicken-and-egg problem in unit tests where DI is not available.
/// </summary>
internal sealed class TestDbAuditWriter : IAuditWriter
{
    private CrudKitDbContext? _db;

    public void Bind(CrudKitDbContext db) => _db = db;

    public async Task WriteAsync(IReadOnlyList<CrudKit.Core.Models.AuditEntry> entries, CancellationToken ct = default)
    {
        if (_db == null) throw new InvalidOperationException("TestDbAuditWriter has not been bound to a DbContext.");
        if (entries.Count == 0) return;

        foreach (var entry in entries)
        {
            _db.AuditLogs.Add(new CrudKit.EntityFrameworkCore.Models.AuditLogEntry
            {
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                Action = entry.Action,
                UserId = entry.UserId,
                Timestamp = entry.Timestamp,
                OldValues = entry.OldValues,
                NewValues = entry.NewValues,
                ChangedFields = entry.ChangedFields,
            });
        }

        _db.IsAuditSave = true;
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        finally
        {
            _db.IsAuditSave = false;
        }
    }
}
