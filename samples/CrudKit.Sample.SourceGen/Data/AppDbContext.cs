using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.Sample.SourceGen.Entities;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Sample.SourceGen.Data;

public class AppDbContext : CrudKitDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Unit> Units => Set<Unit>();
}
