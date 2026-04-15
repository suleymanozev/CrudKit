using CrudKit.Api.Models;
using CrudKit.Api.Tests.Events;
using CrudKit.Api.Tests.Security;
using CrudKit.Api.Tests.Sequencing;
using CrudKit.Api.Tests.ValueObjects;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Api.Tests.Helpers;

public class ApiTestDbContext : CrudKitDbContext
{
    public ApiTestDbContext(
        DbContextOptions<ApiTestDbContext> options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null,
        CrudKitEfOptions? efOptions = null,
        ITenantContext? tenantContext = null,
        IAuditWriter? auditWriter = null,
        IDataFilter<ISoftDeletable>? softDeleteFilter = null,
        IDataFilter<IMultiTenant>? tenantFilter = null,
        IDomainEventDispatcher? domainEventDispatcher = null)
        : base(options, currentUser, timeProvider, efOptions, tenantContext, auditWriter, softDeleteFilter, tenantFilter, domainEventDispatcher) { }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<SoftProductEntity> SoftProducts => Set<SoftProductEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<ConcurrentEntity> Concurrents => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<InvoiceLineEntity> InvoiceLines => Set<InvoiceLineEntity>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<NoFlagEntity> NoFlagEntities => Set<NoFlagEntity>();
    public DbSet<OptOutEntity> OptOutEntities => Set<OptOutEntity>();
    public DbSet<SecuredEntity> SecuredEntities => Set<SecuredEntity>();
    public DbSet<AdminEntity> AdminEntities => Set<AdminEntity>();
    public DbSet<PermissionEntity> PermissionEntities => Set<PermissionEntity>();
    public DbSet<OpAuthEntity> OpAuthEntities => Set<OpAuthEntity>();
    public DbSet<AutoRoutedEntity> AutoRoutedEntities => Set<AutoRoutedEntity>();
    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();
    public DbSet<ProjectTaskEntity> ProjectTasks => Set<ProjectTaskEntity>();
    public DbSet<ProjectMilestoneEntity> ProjectMilestones => Set<ProjectMilestoneEntity>();
    public DbSet<AggregateOrderEntity> AggregateOrders => Set<AggregateOrderEntity>();
    public DbSet<SeqInvoiceEntity> SeqInvoices => Set<SeqInvoiceEntity>();
    public DbSet<PricedItem> PricedItems => Set<PricedItem>();
    public DbSet<SecureItem> SecureItems => Set<SecureItem>();
    public DbSet<TicketEntity> Tickets => Set<TicketEntity>();

    protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PricedItem>(b =>
        {
            b.OwnsOne(e => e.Price);
            b.OwnsOne(e => e.Tax);
        });

        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("__crud_idempotency");
            b.HasKey(e => e.Id);
            b.Property(e => e.Key).HasMaxLength(500).IsRequired();
            b.Property(e => e.Path).HasMaxLength(500).IsRequired();
            b.Property(e => e.Method).HasMaxLength(10).IsRequired();
            b.HasIndex(e => new { e.Key, e.TenantId }).IsUnique();
            b.HasIndex(e => e.ExpiresAt);
        });
    }
}
