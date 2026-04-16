using CrudKit.Core.Interfaces;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Identity.Tests.Helpers;

public class AppUser : IdentityUser { }

// A CrudKit entity living alongside Identity tables
public class Product : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Soft-deletable entity
public class Category : IAuditableEntity, ISoftDeletable
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

public class TestIdentityDbContext : CrudKitIdentityDbContext<AppUser>
{
    public TestIdentityDbContext(DbContextOptions<TestIdentityDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
}
