using CrudKit.Identity.Tests.Helpers;

using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.Identity.Tests;

public class CrudKitIdentityDbContextTests
{
    // ---- Test 1: Identity and CrudKit tables coexist ----

    [Fact]
    public async Task IdentityAndCrudKitTables_Coexist_InSameContext()
    {
        using var db = IdentityDbHelper.CreateDb();

        // Verify Identity tables exist by querying them without error
        var userCount = await db.Users.CountAsync();
        var roleCount = await db.Roles.CountAsync();
        var productCount = await db.Products.CountAsync();

        Assert.Equal(0, userCount);
        Assert.Equal(0, roleCount);
        Assert.Equal(0, productCount);

        // Add a user via Identity's Users DbSet
        var user = new AppUser
        {
            UserName = "testuser",
            Email = "testuser@example.com",
            NormalizedUserName = "TESTUSER",
            NormalizedEmail = "TESTUSER@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Add a CrudKit product entity
        var product = new Product { Name = "Widget" };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        // Both records are persisted
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.Products.CountAsync());
        Assert.NotEqual(Guid.Empty, product.Id);
    }

    // ---- Test 2: CrudKit timestamps work in Identity context ----

    [Fact]
    public async Task SaveChanges_SetsTimestamps_OnCrudKitEntitiesInIdentityContext()
    {
        using var db = IdentityDbHelper.CreateDb();

        // Add a product and save — timestamps should be set
        var product = new Product { Name = "Gadget" };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        Assert.NotEqual(default, product.CreatedAt);
        Assert.NotEqual(default, product.UpdatedAt);

        var originalCreatedAt = product.CreatedAt;
        var originalUpdatedAt = product.UpdatedAt;

        // Small delay to ensure UpdatedAt advances
        await Task.Delay(5);

        // Modify the product and save — UpdatedAt should change, CreatedAt must stay
        product.Name = "Gadget Pro";
        await db.SaveChangesAsync();

        Assert.Equal(originalCreatedAt, product.CreatedAt);
        Assert.True(product.UpdatedAt >= originalUpdatedAt);
    }

    // ---- Test 3: Soft delete works in Identity context ----

    [Fact]
    public async Task Remove_SoftDeletable_SetsDeletedAt_AndHidesRowFromNormalQuery()
    {
        using var db = IdentityDbHelper.CreateDb();

        // Create and persist a category
        var category = new Category { Name = "Electronics" };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        // Remove (soft delete) the category
        db.Categories.Remove(category);
        await db.SaveChangesAsync();

        // Normal query should not find the deleted category (global filter active)
        var found = await db.Categories.FirstOrDefaultAsync(c => c.Id == category.Id);
        Assert.Null(found);

        // IgnoreQueryFilters reveals the row with DeletedAt set
        var raw = await db.Categories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == category.Id);
        Assert.NotNull(raw);
        Assert.NotNull(raw!.DeletedAt);
    }
}
