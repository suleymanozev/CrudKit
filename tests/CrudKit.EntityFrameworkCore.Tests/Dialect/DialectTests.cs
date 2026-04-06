using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using System.Linq.Expressions;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Dialect;


public class DialectTests
{
    private static Expression<Func<PersonEntity, string>> NameExpr()
        => e => e.Name;

    [Fact]
    public void GenericDialect_ApplyLike_FiltersCorrectly()
    {
        var dialect = new GenericDialect();
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Alice" },
            new PersonEntity { Id = Guid.NewGuid(), Name = "Bob" },
            new PersonEntity { Id = Guid.NewGuid(), Name = "ALICE" },
        }.AsQueryable();

        var result = dialect.ApplyLike(source, NameExpr(), "alice");

        var names = result.Select(e => e.Name).ToList();
        Assert.Contains("Alice", names);
        Assert.Contains("ALICE", names);
        Assert.DoesNotContain("Bob", names);
    }

    [Fact]
    public void GenericDialect_ApplyStartsWith_FiltersCorrectly()
    {
        var dialect = new GenericDialect();
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Alice" },
            new PersonEntity { Id = Guid.NewGuid(), Name = "Bob" },
            new PersonEntity { Id = Guid.NewGuid(), Name = "alan" },
        }.AsQueryable();

        var result = dialect.ApplyStartsWith(source, NameExpr(), "al");

        var names = result.Select(e => e.Name).ToList();
        Assert.Contains("Alice", names);
        Assert.Contains("alan", names);
        Assert.DoesNotContain("Bob", names);
    }

    [Fact]
    public void SqliteDialect_ApplyLike_FiltersCorrectly()
    {
        var dialect = new SqliteDialect();
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Istanbul" },
            new PersonEntity { Id = Guid.NewGuid(), Name = "Ankara" },
        }.AsQueryable();

        var result = dialect.ApplyLike(source, NameExpr(), "stan");

        Assert.Single(result);
        Assert.Equal("Istanbul", result.First().Name);
    }

    [Fact]
    public void SqliteDialect_ApplyStartsWith_FiltersCorrectly()
    {
        var dialect = new SqliteDialect();
        var source = new[]
        {
            new PersonEntity { Id = Guid.NewGuid(), Name = "Istanbul" },
            new PersonEntity { Id = Guid.NewGuid(), Name = "Ankara" },
        }.AsQueryable();

        var result = dialect.ApplyStartsWith(source, NameExpr(), "ist");

        Assert.Single(result);
        Assert.Equal("Istanbul", result.First().Name);
    }

    [Fact]
    public void GenericDialect_GetUpsertSql_ContainsOnConflict()
    {
        var dialect = new GenericDialect();
        var sql = dialect.GetUpsertSql("test_table", ["col1", "col2"], ["col1"]);
        Assert.Contains("ON CONFLICT", sql);
        Assert.Contains("test_table", sql);
    }

    [Fact]
    public void DialectDetector_DetectsSqlite_ForSqliteProvider()
    {
        using var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void SqliteDialect_GetUpsertSql_ReturnsValidSql()
    {
        var dialect = new SqliteDialect();
        var sql = dialect.GetUpsertSql("products", ["name", "price", "qty"], ["name"]);

        Assert.Contains("INSERT INTO products", sql);
        Assert.Contains("VALUES (@p0, @p1, @p2)", sql);
        Assert.Contains("ON CONFLICT (name)", sql);
        Assert.Contains("DO UPDATE SET", sql);
        Assert.Contains("name = EXCLUDED.name", sql);
        Assert.Contains("price = EXCLUDED.price", sql);
    }

    [Fact]
    public void SqliteDialect_ConfigureConcurrencyToken_ConfiguresToken()
    {
        // Verify via an integration test using the DbHelper which uses SQLite
        using var db = DbHelper.CreateDb();
        var entityType = db.Model.FindEntityType(typeof(ConcurrentEntity));
        Assert.NotNull(entityType);

        var rowVersionProp = entityType!.FindProperty(nameof(ConcurrentEntity.RowVersion));
        Assert.NotNull(rowVersionProp);
        Assert.True(rowVersionProp!.IsConcurrencyToken);
    }
}
