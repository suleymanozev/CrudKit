using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Enums;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

/// <summary>
/// Tests for IncludeApplier: attribute metadata reading, "none" override,
/// explicit client override, and scope filtering.
/// </summary>
public class IncludeApplierTests
{
    // --- Test entity fixtures ---

    [DefaultInclude("Lines")]
    [DefaultInclude("Notes", Scope = IncludeScope.DetailOnly)]
    private class OrderEntity
    {
        public string Id { get; set; } = string.Empty;
        // Navigation properties are not added here because we only test
        // attribute metadata and queryable pass-through (no real DB).
    }

    [DefaultInclude("Lines")]
    [DefaultInclude("Tags")]
    private class ProductEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;  // scalar — valid prop name, just not a nav
        public string Lines { get; set; } = string.Empty;
    }

    private class PlainEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    // --- Attribute metadata tests ---

    [Fact]
    public void OrderEntity_HasTwoDefaultIncludeAttributes()
    {
        var attrs = typeof(OrderEntity).GetCustomAttributes<DefaultIncludeAttribute>(inherit: true).ToList();
        Assert.Equal(2, attrs.Count);
    }

    [Fact]
    public void OrderEntity_LinesAttribute_HasScopeAll()
    {
        var attr = typeof(OrderEntity)
            .GetCustomAttributes<DefaultIncludeAttribute>(inherit: true)
            .Single(a => a.NavigationProperty == "Lines");

        Assert.Equal(IncludeScope.All, attr.Scope);
    }

    [Fact]
    public void OrderEntity_NotesAttribute_HasScopeDetailOnly()
    {
        var attr = typeof(OrderEntity)
            .GetCustomAttributes<DefaultIncludeAttribute>(inherit: true)
            .Single(a => a.NavigationProperty == "Notes");

        Assert.Equal(IncludeScope.DetailOnly, attr.Scope);
    }

    // --- IncludeApplier behaviour tests ---
    // We use an empty in-memory IQueryable<T> (no DB needed) because
    // IncludeApplier.Apply only chains .Include() extension calls which are
    // lazy — no translation occurs until materialized.

    [Fact]
    public void Apply_NoneParam_ReturnsQueryUnchanged()
    {
        var source = Array.Empty<PlainEntity>().AsQueryable();
        var result = IncludeApplier.Apply(source, "none", isDetailQuery: false);

        // "none" must return the exact same queryable reference with no Include appended
        Assert.Same(source, result);
    }

    [Fact]
    public void Apply_NoneParam_IsCaseInsensitive()
    {
        var source = Array.Empty<PlainEntity>().AsQueryable();
        var result = IncludeApplier.Apply(source, "NONE", isDetailQuery: false);
        Assert.Same(source, result);
    }

    [Fact]
    public void Apply_ExplicitIncludeParam_AcceptsValidPropertyNamesWithoutThrowing()
    {
        // ProductEntity has [DefaultInclude("Lines")] and [DefaultInclude("Tags")].
        // When an explicit param is given, only the requested (and valid) prop should be used.
        // We verify the method does not throw and returns a non-null queryable.
        var source = Array.Empty<ProductEntity>().AsQueryable();

        var result = IncludeApplier.Apply(source, "Tags,Lines", isDetailQuery: false);

        Assert.NotNull(result);
    }

    [Fact]
    public void Apply_ExplicitIncludeParam_SkipsUnknownPropertyNames()
    {
        var source = Array.Empty<PlainEntity>().AsQueryable();

        // "Bogus" is not a property on PlainEntity — should be silently ignored
        var result = IncludeApplier.Apply(source, "Bogus", isDetailQuery: false);

        // No includes applied, but query itself is still returned
        Assert.NotNull(result);
    }

    [Fact]
    public void Apply_NullParam_ListQuery_SkipsDetailOnlyAttributes()
    {
        // For a list query (isDetailQuery: false), DetailOnly attributes must be skipped.
        // We cannot easily inspect the EF Include expression tree without a DbContext,
        // so we verify the method completes without throwing (smoke test for scope logic).
        var source = Array.Empty<OrderEntity>().AsQueryable();
        var result = IncludeApplier.Apply(source, null, isDetailQuery: false);
        Assert.NotNull(result);
    }

    [Fact]
    public void Apply_NullParam_DetailQuery_AppliesAllAttributes()
    {
        // For a detail query (isDetailQuery: true), all attributes including DetailOnly should apply.
        var source = Array.Empty<OrderEntity>().AsQueryable();
        var result = IncludeApplier.Apply(source, null, isDetailQuery: true);
        Assert.NotNull(result);
    }

    [Fact]
    public void Apply_NullParam_PlainEntity_ReturnsQueryWithNoChange()
    {
        // An entity with no [DefaultInclude] attributes returns the same queryable.
        var source = Array.Empty<PlainEntity>().AsQueryable();
        var result = IncludeApplier.Apply(source, null, isDetailQuery: false);
        Assert.Same(source, result);
    }

    // -----------------------------------------------------------------------
    // Integration tests — real SQLite in-memory DB to verify actual includes
    // -----------------------------------------------------------------------

    /// <summary>
    /// Seeds a <see cref="ParentEntity"/> with one child and one note, then returns
    /// the seeded parent's Id.
    /// </summary>
    private static string SeedParentWithChildAndNote(TestDbContext db)
    {
        var parent = new ParentEntity { Title = "Integration parent" };
        db.Parents.Add(parent);
        db.SaveChanges();

        db.Children.Add(new ChildEntity { Name = "Child 1", ParentId = parent.Id });
        db.Notes.Add(new NoteEntity { Content = "Note 1", ParentId = parent.Id });
        db.SaveChanges();

        return parent.Id;
    }

    [Fact]
    public async Task Apply_ListQuery_LoadsChildrenOnly_NotesRemainNull()
    {
        // Arrange: seed data in an isolated SQLite in-memory database
        await using var db = DbHelper.CreateDb();
        var parentId = SeedParentWithChildAndNote(db);

        // Act: isDetailQuery = false → only Scope.All includes (Children) should load
        var query = IncludeApplier.Apply(
            db.Parents.AsNoTracking(),
            includeParam: null,
            isDetailQuery: false);

        var parent = await query.SingleAsync(p => p.Id == parentId);

        // Assert: Children navigation was included — it must contain the seeded child
        Assert.NotNull(parent.Children);
        Assert.Single(parent.Children);

        // Notes navigation was NOT included — AsNoTracking prevents lazy load,
        // so the collection must remain at its default empty state (not populated from DB)
        // EF Core initialises the backing list to an empty collection on AsNoTracking
        // without an Include, so Count == 0 proves no DB data was loaded.
        Assert.Empty(parent.Notes);
    }

    [Fact]
    public async Task Apply_DetailQuery_LoadsBothChildrenAndNotes()
    {
        // Arrange: fresh database with the same seed data
        await using var db = DbHelper.CreateDb();
        var parentId = SeedParentWithChildAndNote(db);

        // Act: isDetailQuery = true → both Scope.All and DetailOnly includes should load
        var query = IncludeApplier.Apply(
            db.Parents.AsNoTracking(),
            includeParam: null,
            isDetailQuery: true);

        var parent = await query.SingleAsync(p => p.Id == parentId);

        // Assert: Children navigation was included
        Assert.NotNull(parent.Children);
        Assert.Single(parent.Children);

        // Assert: Notes navigation was also included (DetailOnly scope honoured)
        Assert.NotNull(parent.Notes);
        Assert.Single(parent.Notes);
    }
}
