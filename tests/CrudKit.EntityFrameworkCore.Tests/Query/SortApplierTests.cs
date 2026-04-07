using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

public class SortApplierTests
{
    private static IQueryable<PersonEntity> Source() => new[]
    {
        new PersonEntity { Id = Guid.NewGuid(), Name = "Charlie", Age = 25 },
        new PersonEntity { Id = Guid.NewGuid(), Name = "Alice",   Age = 30 },
        new PersonEntity { Id = Guid.NewGuid(), Name = "Bob",     Age = 25 },
    }.AsQueryable();

    [Fact]
    public void Apply_SingleField_Asc()
    {
        var result = SortApplier.Apply(Source(), "name").ToList();
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_SingleField_Desc()
    {
        var result = SortApplier.Apply(Source(), "-name").ToList();
        Assert.Equal(new[] { "Charlie", "Bob", "Alice" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_MultipleFields_AgeAscThenNameAsc()
    {
        var result = SortApplier.Apply(Source(), "age,name").ToList();
        // Age 25: Charlie, Bob -> sorted by name: Bob, Charlie; then Alice (30)
        Assert.Equal(new[] { "Bob", "Charlie", "Alice" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_MultipleFields_AgeDescThenNameAsc()
    {
        var result = SortApplier.Apply(Source(), "-age,name").ToList();
        // Age 30: Alice; Age 25: Bob, Charlie
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_NullOrEmpty_DefaultsToCreatedAtDesc()
    {
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "A", CreatedAt = new DateTime(2025, 1, 1) },
            new PersonEntity { Id = Guid.NewGuid(), Name = "B", CreatedAt = new DateTime(2026, 1, 1) },
        }.AsQueryable();

        var result = SortApplier.Apply(source, null).ToList();
        Assert.Equal("B", result[0].Name); // newest first
    }

    [Fact]
    public void Apply_InvalidField_IsIgnored()
    {
        // "nonexistent" is skipped, "name" asc is applied
        var result = SortApplier.Apply(Source(), "nonexistent,name").ToList();
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, result.Select(e => e.Name));
    }

    [Fact]
    public void Apply_SnakeCaseField_IsResolved()
    {
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "A", CreatedAt = new DateTime(2025, 1, 1) },
            new PersonEntity { Id = Guid.NewGuid(), Name = "B", CreatedAt = new DateTime(2026, 1, 1) },
        }.AsQueryable();

        var result = SortApplier.Apply(source, "created_at").ToList();
        Assert.Equal("A", result[0].Name); // oldest first (ASC)
    }

    // ---- [Sortable] / [NotSortable] attribute tests ----

    [Fact]
    public void NotSortable_Property_SkipsSort()
    {
        // PartiallySortableEntity.Rank has [NotSortable] — sort on Rank must be ignored,
        // resulting in the default CreatedAt DESC fallback.
        var source = new[]
        {
            new PartiallySortableEntity { Id = Guid.NewGuid(), Name = "Alice", Rank = 3, CreatedAt = new DateTime(2025, 1, 1) },
            new PartiallySortableEntity { Id = Guid.NewGuid(), Name = "Bob",   Rank = 1, CreatedAt = new DateTime(2026, 1, 1) },
            new PartiallySortableEntity { Id = Guid.NewGuid(), Name = "Carol", Rank = 2, CreatedAt = new DateTime(2024, 1, 1) },
        }.AsQueryable();

        // Sorting by Rank should be silently ignored; result falls back to CreatedAt DESC
        var result = SortApplier.Apply(source, "rank").ToList();
        Assert.Equal("Bob", result[0].Name);   // newest CreatedAt first
        Assert.Equal("Alice", result[1].Name);
        Assert.Equal("Carol", result[2].Name);
    }

    [Fact]
    public void NotSortable_Entity_SkipsAllSorts()
    {
        // EntityLevelNotSortableEntity has [NotSortable] on the class —
        // sorting by Rank (no override) must be silently ignored.
        var source = new[]
        {
            new EntityLevelNotSortableEntity { Id = Guid.NewGuid(), Name = "Alice", Rank = 3, CreatedAt = new DateTime(2025, 1, 1) },
            new EntityLevelNotSortableEntity { Id = Guid.NewGuid(), Name = "Bob",   Rank = 1, CreatedAt = new DateTime(2026, 1, 1) },
            new EntityLevelNotSortableEntity { Id = Guid.NewGuid(), Name = "Carol", Rank = 2, CreatedAt = new DateTime(2024, 1, 1) },
        }.AsQueryable();

        // Rank sort is ignored; CreatedAt DESC is the fallback
        var result = SortApplier.Apply(source, "rank").ToList();
        Assert.Equal("Bob", result[0].Name);
    }

    [Fact]
    public void Sortable_Property_OverridesEntityNotSortable()
    {
        // EntityLevelNotSortableEntity has [NotSortable] on the class,
        // but Name has [Sortable] — sorting by Name must work.
        var source = new[]
        {
            new EntityLevelNotSortableEntity { Id = Guid.NewGuid(), Name = "Charlie", Rank = 3 },
            new EntityLevelNotSortableEntity { Id = Guid.NewGuid(), Name = "Alice",   Rank = 1 },
            new EntityLevelNotSortableEntity { Id = Guid.NewGuid(), Name = "Bob",     Rank = 2 },
        }.AsQueryable();

        var result = SortApplier.Apply(source, "name").ToList();
        Assert.Equal(new[] { "Alice", "Bob", "Charlie" }, result.Select(e => e.Name));
    }
}
