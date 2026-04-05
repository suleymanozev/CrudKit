using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

public class FilterApplierTests
{
    private static IQueryable<PersonEntity> Source() => new[]
    {
        new PersonEntity { Id = Guid.NewGuid(), Name = "Alice", Age = 30 },
        new PersonEntity { Id = Guid.NewGuid(), Name = "Bob",   Age = 25 },
        new PersonEntity { Id = Guid.NewGuid(), Name = "Carol", Age = 30 },
        new PersonEntity { Id = Guid.NewGuid(), Name = "alice", Age = 20 },
    }.AsQueryable();

    private static FilterApplier Applier() => new(new GenericDialect());

    [Fact]
    public void Apply_Eq_FiltersExact()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("Bob"));
        Assert.Single(result);
        Assert.Equal("Bob", result.First().Name);
    }

    [Fact]
    public void Apply_Neq_ExcludesValue()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("neq:Bob"));
        Assert.Equal(3, result.Count());
        Assert.DoesNotContain(result, e => e.Name == "Bob");
    }

    [Fact]
    public void Apply_Gt_FiltersGreaterThan()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("gt:25"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age > 25));
    }

    [Fact]
    public void Apply_Gte_FiltersGreaterThanOrEqual()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("gte:30"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age >= 30));
    }

    [Fact]
    public void Apply_Lt_FiltersLessThan()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("lt:30"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age < 30));
    }

    [Fact]
    public void Apply_Lte_FiltersLessThanOrEqual()
    {
        var result = Applier().Apply(Source(), "Age", FilterOp.Parse("lte:25"));
        Assert.Equal(2, result.Count());
        Assert.All(result, e => Assert.True(e.Age <= 25));
    }

    [Fact]
    public void Apply_Like_FiltersContains_CaseInsensitive()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("like:alic"));
        Assert.Equal(2, result.Count()); // Alice + alice
    }

    [Fact]
    public void Apply_Starts_FiltersStartsWith()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("starts:ali"));
        Assert.Equal(2, result.Count()); // Alice + alice
    }

    [Fact]
    public void Apply_In_FiltersMultipleValues()
    {
        var result = Applier().Apply(Source(), "Name", FilterOp.Parse("in:Alice,Bob"));
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public void Apply_Null_FiltersNullValues()
    {
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Alice", Age = 30 },
            new PersonEntity { Id = Guid.NewGuid(), Name = null!, Age = 25 },
        }.AsQueryable();
        var result = Applier().Apply(source, "Name", FilterOp.Parse("null:"));
        Assert.Single(result);
    }

    [Fact]
    public void Apply_Notnull_FiltersNonNullValues()
    {
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Alice", Age = 30 },
            new PersonEntity { Id = Guid.NewGuid(), Name = null!, Age = 25 },
        }.AsQueryable();
        var result = Applier().Apply(source, "Name", FilterOp.Parse("notnull:"));
        Assert.Single(result);
        Assert.Equal("Alice", result.First().Name);
    }

    [Fact]
    public void Apply_UnknownProperty_IsIgnored()
    {
        var result = Applier().Apply(Source(), "NonExistentField", FilterOp.Parse("eq:hack"));
        Assert.Equal(4, result.Count()); // no filter applied
    }

    [Fact]
    public void Apply_SnakeCasePropertyName_IsMatched()
    {
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Alice", Age = 30, CreatedAt = new DateTime(2026, 1, 1) },
            new PersonEntity { Id = Guid.NewGuid(), Name = "Bob",   Age = 25, CreatedAt = new DateTime(2025, 1, 1) },
        }.AsQueryable();
        var result = Applier().Apply(source, "created_at", FilterOp.Parse("gt:2025-06-01"));
        Assert.Single(result);
        Assert.Equal("Alice", result.First().Name);
    }
}
