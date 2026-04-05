using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Query;

/// <summary>
/// Minimal DbContext used only for QueryBuilder tests.
/// Backed by SQLite in-memory so EF Core async operations work correctly.
/// </summary>
public class QueryBuilderTestDb : DbContext
{
    public QueryBuilderTestDb(DbContextOptions<QueryBuilderTestDb> options) : base(options) { }
    public DbSet<PersonEntity> Persons => Set<PersonEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Use string primary key matching PersonEntity
        modelBuilder.Entity<PersonEntity>().HasKey(e => e.Id);
    }
}

public class QueryBuilderTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private QueryBuilderTestDb _db = null!;

    /// <summary>Creates an isolated SQLite in-memory database per test class instance.</summary>
    public async Task InitializeAsync()
    {
        // Keep the connection open so the in-memory database persists for the test
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<QueryBuilderTestDb>()
            .UseSqlite(_connection)
            .Options;

        _db = new QueryBuilderTestDb(options);
        await _db.Database.EnsureCreatedAsync();

        // Seed 10 persons
        var persons = Enumerable.Range(1, 10)
            .Select(i => new PersonEntity
            {
                Id = Guid.NewGuid(),
                Name = $"Person{i:D2}",
                Age = 20 + i,
                CreatedAt = new DateTime(2026, 1, i),
            })
            .ToList();

        _db.Persons.AddRange(persons);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private QueryBuilder<PersonEntity> Builder()
        => new(new FilterApplier(new GenericDialect()));

    [Fact]
    public async Task Apply_ReturnsAllItems_WhenNoFilters()
    {
        var result = await Builder().Apply(_db.Persons, new ListParams(), CancellationToken.None);
        Assert.Equal(10, result.Total);
        Assert.Equal(10, result.Data.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PerPage);
    }

    [Fact]
    public async Task Apply_Pagination_ReturnsCorrectPage()
    {
        var lp = new ListParams { Page = 2, PerPage = 3 };
        var result = await Builder().Apply(_db.Persons, lp, CancellationToken.None);

        Assert.Equal(10, result.Total);
        Assert.Equal(3, result.Data.Count);
        Assert.Equal(2, result.Page);
        Assert.Equal(3, result.PerPage);
        Assert.Equal(4, result.TotalPages); // ceil(10/3)
    }

    [Fact]
    public async Task Apply_Filter_ReducesTotal()
    {
        var lp = new ListParams
        {
            Filters = { ["Age"] = FilterOp.Parse("gte:28") },
        };
        var result = await Builder().Apply(_db.Persons, lp, CancellationToken.None);
        Assert.Equal(3, result.Total); // ages 28, 29, 30
        Assert.All(result.Data, e => Assert.True(e.Age >= 28));
    }

    [Fact]
    public async Task Apply_Sort_OrdersCorrectly()
    {
        var lp = new ListParams { Sort = "-age" };
        var result = await Builder().Apply(_db.Persons, lp, CancellationToken.None);
        var ages = result.Data.Select(e => e.Age).ToList();
        Assert.Equal(ages.OrderByDescending(a => a).ToList(), ages);
    }

    [Fact]
    public async Task Apply_LastPage_HasFewerItems()
    {
        var lp = new ListParams { Page = 4, PerPage = 3 };
        var result = await Builder().Apply(_db.Persons, lp, CancellationToken.None);
        Assert.Single(result.Data); // only item 10
    }
}
