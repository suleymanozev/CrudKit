using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.Sample.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Sample.Api.Data;

public class SampleDbContext : CrudKitDbContext
{
    public SampleDbContext(DbContextOptions<SampleDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public new DbSet<AuditLog> AuditLogs => Set<AuditLog>();
}
