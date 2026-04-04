using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Api.Tests.Helpers;

public class ApiTestDbContext : CrudKitDbContext
{
    public ApiTestDbContext(DbContextOptions<ApiTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<SoftProductEntity> SoftProducts => Set<SoftProductEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<ConcurrentEntity> Concurrents => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<InvoiceLineEntity> InvoiceLines => Set<InvoiceLineEntity>();
}
