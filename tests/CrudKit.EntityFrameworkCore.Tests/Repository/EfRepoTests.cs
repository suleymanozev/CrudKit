// tests/CrudKit.EntityFrameworkCore.Tests/Repository/EfRepoTests.cs
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;
using CrudKit.EntityFrameworkCore.Query;
using CrudKit.EntityFrameworkCore.Repository;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Repository;

public class EfRepoTests
{
    private static (TestDbContext db, EfRepo<PersonEntity> repo) CreatePersonRepo(
        ICurrentUser? user = null)
    {
        var db = DbHelper.CreateDb(user);
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<PersonEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<PersonEntity>(db, queryBuilder);
        return (db, repo);
    }

    private static (TestDbContext db, EfRepo<SoftPersonEntity> repo) CreateSoftRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<SoftPersonEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<SoftPersonEntity>(db, queryBuilder);
        return (db, repo);
    }

    private static (TestDbContext db, EfRepo<UserEntity> repo) CreateUserRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<UserEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<UserEntity>(db, queryBuilder);
        return (db, repo);
    }

    // ---- Create ----

    [Fact]
    public async Task Create_MapsDto_AndReturnsEntity()
    {
        var (db, repo) = CreatePersonRepo();
        var dto = new { Name = "Alice", Age = 30 };

        var result = await repo.Create(dto);

        Assert.NotEmpty(result.Id);
        Assert.Equal("Alice", result.Name);
        Assert.Equal(30, result.Age);
        Assert.True(await db.Persons.AnyAsync(e => e.Id == result.Id));
    }

    [Fact]
    public async Task Create_WithHashedField_StoresHash_AndNullsInResponse()
    {
        var (_, repo) = CreateUserRepo();
        var dto = new { Username = "alice", PasswordHash = "secret123" };

        var result = await repo.Create(dto);

        Assert.Equal("alice", result.Username);
        Assert.Null(result.PasswordHash); // SkipResponse clears it from returned object
    }

    // ---- FindById ----

    [Fact]
    public async Task FindById_ReturnsEntity_WhenExists()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Bob", Age = 25 });

        var found = await repo.FindById(created.Id);
        Assert.Equal("Bob", found.Name);
    }

    [Fact]
    public async Task FindById_ThrowsNotFound_WhenMissing()
    {
        var (_, repo) = CreatePersonRepo();
        var ex = await Assert.ThrowsAsync<AppError>(() => repo.FindById("non-existent"));
        Assert.Equal(404, ex.StatusCode);
    }

    // ---- FindByIdOrDefault ----

    [Fact]
    public async Task FindByIdOrDefault_ReturnsNull_WhenMissing()
    {
        var (_, repo) = CreatePersonRepo();
        var result = await repo.FindByIdOrDefault("non-existent");
        Assert.Null(result);
    }

    // ---- Update ----

    [Fact]
    public async Task Update_AppliesOnlyProvidedFields()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Alice", Age = 30 });

        // Partial update — only Age is changed (Name absent = skip)
        var updateDto = new { Age = 31 };
        var updated = await repo.Update(created.Id, updateDto);

        Assert.Equal("Alice", updated.Name); // unchanged
        Assert.Equal(31, updated.Age);
    }

    [Fact]
    public async Task Update_WithOptional_SkipsAbsentFields()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Alice", Age = 30 });

        // Optional<string> Undefined → should not touch Name
        var updateDto = new
        {
            Name = Optional<string>.Undefined,
            Age = (Optional<int>)32,
        };
        var updated = await repo.Update(created.Id, updateDto);

        Assert.Equal("Alice", updated.Name);
        Assert.Equal(32, updated.Age);
    }

    // ---- Delete ----

    [Fact]
    public async Task Delete_PhysicalEntity_RemovesRow()
    {
        var (db, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Carol", Age = 22 });

        await repo.Delete(created.Id);

        Assert.False(await db.Persons.AnyAsync(e => e.Id == created.Id));
    }

    [Fact]
    public async Task Delete_SoftDeletable_SetsDeletedAt_RowStillExists()
    {
        var (db, repo) = CreateSoftRepo();
        var created = await repo.Create(new { Name = "Dave" });

        await repo.Delete(created.Id);

        var raw = await db.SoftPersons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == created.Id);
        Assert.NotNull(raw);
        Assert.NotNull(raw!.DeletedAt);
    }

    // ---- Restore ----

    [Fact]
    public async Task Restore_SoftDeletable_ClearsDeletedAt()
    {
        var (db, repo) = CreateSoftRepo();
        var created = await repo.Create(new { Name = "Eve" });
        await repo.Delete(created.Id);

        await repo.Restore(created.Id);

        var restored = await db.SoftPersons.FirstOrDefaultAsync(e => e.Id == created.Id);
        Assert.NotNull(restored);
        Assert.Null(restored!.DeletedAt);
    }

    // ---- List ----

    [Fact]
    public async Task List_ReturnsPaginatedResult()
    {
        var (_, repo) = CreatePersonRepo();
        for (var i = 1; i <= 5; i++)
            await repo.Create(new { Name = $"Person{i}", Age = 20 + i });

        var result = await repo.List(new ListParams { Page = 1, PerPage = 3 });

        Assert.Equal(5, result.Total);
        Assert.Equal(3, result.Data.Count);
    }

    // ---- Exists + Count ----

    [Fact]
    public async Task Exists_ReturnsTrueForExistingId()
    {
        var (_, repo) = CreatePersonRepo();
        var created = await repo.Create(new { Name = "Frank", Age = 40 });
        Assert.True(await repo.Exists(created.Id));
        Assert.False(await repo.Exists("non-existent"));
    }

    [Fact]
    public async Task Count_ReturnsEntityCount()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "G1", Age = 20 });
        await repo.Create(new { Name = "G2", Age = 21 });
        Assert.Equal(2, await repo.Count());
    }

    // ---- FindByField ----

    [Fact]
    public async Task FindByField_ReturnsMatchingEntities()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "Helen", Age = 35 });
        await repo.Create(new { Name = "Ivan",  Age = 35 });
        await repo.Create(new { Name = "Julia", Age = 28 });

        var result = await repo.FindByField("Age", 35);
        Assert.Equal(2, result.Count);
    }

    // ---- ICrudHooks.ApplyScope integration ----

    /// <summary>
    /// Hooks implementation that filters PersonEntity rows by minimum age.
    /// </summary>
    private class AgeFilterHooks : ICrudHooks<PersonEntity>
    {
        private readonly int _minAge;
        public AgeFilterHooks(int minAge) => _minAge = minAge;

        public IQueryable<PersonEntity> ApplyScope(IQueryable<PersonEntity> query,
            CrudKit.Core.Context.AppContext ctx)
            => query.Where(p => p.Age >= _minAge);
    }

    private static (TestDbContext db, EfRepo<PersonEntity> repo) CreatePersonRepoWithHooks(
        ICrudHooks<PersonEntity> hooks)
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var queryBuilder = new QueryBuilder<PersonEntity>(new FilterApplier(dialect));
        var repo = new EfRepo<PersonEntity>(db, queryBuilder, hooks);
        return (db, repo);
    }

    [Fact]
    public async Task List_WithHooks_AppliesScope()
    {
        // Arrange: three people with ages 20, 25, 30; scope filters to Age >= 25
        var (_, repo) = CreatePersonRepoWithHooks(new AgeFilterHooks(minAge: 25));
        await repo.Create(new { Name = "Young",  Age = 20 });
        await repo.Create(new { Name = "Middle", Age = 25 });
        await repo.Create(new { Name = "Senior", Age = 30 });

        // Act
        var result = await repo.List(new ListParams { Page = 1, PerPage = 10 });

        // Assert: only the two people with Age >= 25 are returned
        Assert.Equal(2, result.Total);
        Assert.All(result.Data, p => Assert.True(p.Age >= 25));
    }

    [Fact]
    public async Task FindById_WithHooks_AppliesScope_ThrowsWhenOutOfScope()
    {
        // Arrange: entity with Age = 20 is outside the scope (Age >= 25)
        var (_, repo) = CreatePersonRepoWithHooks(new AgeFilterHooks(minAge: 25));
        var created = await repo.Create(new { Name = "OutOfScope", Age = 20 });

        // Act & Assert: FindById must throw NotFound because scope excludes this entity
        var ex = await Assert.ThrowsAsync<AppError>(() => repo.FindById(created.Id));
        Assert.Equal(404, ex.StatusCode);
    }
}
