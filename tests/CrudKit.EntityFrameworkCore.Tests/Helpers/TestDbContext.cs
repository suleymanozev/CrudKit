using CrudKit.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Concrete DbContext for tests. Inherits CrudKitDbContext.
/// Owns the SqliteConnection and disposes it when the context is disposed.
/// </summary>
public class TestDbContext : CrudKitDbContext
{
    private readonly SqliteConnection? _connection;

    public TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser,
        SqliteConnection? connection = null, TimeProvider? timeProvider = null)
        : base(options, currentUser, timeProvider)
    {
        _connection = connection;
    }

    public DbSet<PersonEntity> Persons => Set<PersonEntity>();
    public DbSet<SoftPersonEntity> SoftPersons => Set<SoftPersonEntity>();
    public DbSet<TenantPersonEntity> TenantPersons => Set<TenantPersonEntity>();
    public DbSet<AuditPersonEntity> AuditPersons => Set<AuditPersonEntity>();
    public DbSet<ConcurrentEntity> ConcurrentEntities => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();

    // DbSets for IncludeApplier integration tests
    public DbSet<ParentEntity> Parents => Set<ParentEntity>();
    public DbSet<ChildEntity> Children => Set<ChildEntity>();
    public DbSet<NoteEntity> Notes => Set<NoteEntity>();

    public override void Dispose()
    {
        base.Dispose();
        _connection?.Dispose();
    }

    public override ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        return base.DisposeAsync();
    }
}
