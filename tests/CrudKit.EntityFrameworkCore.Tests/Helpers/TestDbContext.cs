using CrudKit.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Tests.Helpers;

/// <summary>
/// Concrete DbContext for tests. Inherits CrudKitDbContext.
/// Only adds DbSets — no configuration needed.
/// </summary>
public class TestDbContext : CrudKitDbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<PersonEntity> Persons => Set<PersonEntity>();
    public DbSet<SoftPersonEntity> SoftPersons => Set<SoftPersonEntity>();
    public DbSet<TenantPersonEntity> TenantPersons => Set<TenantPersonEntity>();
    public DbSet<AuditPersonEntity> AuditPersons => Set<AuditPersonEntity>();
    public DbSet<ConcurrentEntity> ConcurrentEntities => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
}
