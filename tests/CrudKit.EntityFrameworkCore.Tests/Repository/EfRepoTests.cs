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
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<PersonEntity>(filterApplier);
        var repo = new EfRepo<PersonEntity>(db, queryBuilder, filterApplier);
        return (db, repo);
    }

    private static (TestDbContext db, EfRepo<SoftPersonEntity> repo) CreateSoftRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<SoftPersonEntity>(filterApplier);
        var repo = new EfRepo<SoftPersonEntity>(db, queryBuilder, filterApplier);
        return (db, repo);
    }

    private static (TestDbContext db, EfRepo<UserEntity> repo) CreateUserRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<UserEntity>(filterApplier);
        var repo = new EfRepo<UserEntity>(db, queryBuilder, filterApplier);
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

    private static (TestDbContext db, EfRepo<UniqueCodeEntity> repo) CreateUniqueCodeRepo()
    {
        var db = DbHelper.CreateDb();
        var dialect = DialectDetector.Detect(db);
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<UniqueCodeEntity>(filterApplier);
        var repo = new EfRepo<UniqueCodeEntity>(db, queryBuilder, filterApplier);
        return (db, repo);
    }

    // ---- Restore — unique constraint checks ----

    [Fact]
    public async Task Restore_ThrowsConflict_WhenUniqueFieldClashesWithActiveRecord()
    {
        // Arrange: create entity with code "ABC", soft-delete it,
        // then create another active entity with the same code
        var (_, repo) = CreateUniqueCodeRepo();
        var first = await repo.Create(new { Code = "ABC", Name = "First" });
        await repo.Delete(first.Id);
        await repo.Create(new { Code = "ABC", Name = "Second" }); // active record

        // Act & Assert: restoring first should fail with 409 Conflict
        var ex = await Assert.ThrowsAsync<AppError>(() => repo.Restore(first.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Restore_Succeeds_WhenUniqueFieldDoesNotClash()
    {
        // Arrange: create entity, soft-delete it — no other record with same code
        var (_, repo) = CreateUniqueCodeRepo();
        var entity = await repo.Create(new { Code = "XYZ", Name = "Solo" });
        await repo.Delete(entity.Id);

        // Act: restore should succeed without exception
        await repo.Restore(entity.Id);

        // Assert: entity is active again
        var restored = await repo.FindById(entity.Id);
        Assert.Null(restored.DeletedAt);
    }

    [Fact]
    public async Task Restore_Succeeds_WhenUniqueFieldClashesWithDeletedRecord()
    {
        // Arrange: create two entities with different codes, delete both
        var (_, repo) = CreateUniqueCodeRepo();
        var first  = await repo.Create(new { Code = "DEF", Name = "Alpha" });
        var second = await repo.Create(new { Code = "GHI", Name = "Beta" });
        await repo.Delete(first.Id);
        await repo.Delete(second.Id);

        // Act: restoring first — no active record conflicts, other record is also deleted
        await repo.Restore(first.Id);

        // Assert: first is active again
        var restored = await repo.FindById(first.Id);
        Assert.Null(restored.DeletedAt);
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
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<PersonEntity>(filterApplier);
        var repo = new EfRepo<PersonEntity>(db, queryBuilder, filterApplier, hooks);
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

    // ---- BulkCount ----

    [Fact]
    public async Task BulkCount_ReturnsMatchingCount()
    {
        var (_, repo) = CreatePersonRepo();
        for (var i = 1; i <= 5; i++)
            await repo.Create(new { Name = $"Person{i}", Age = 20 + i });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = new FilterOp { Operator = "gte", Value = "23" }
        };

        var count = await repo.BulkCount(filters);
        Assert.Equal(3, count); // Ages 23, 24, 25
    }

    [Fact]
    public async Task BulkCount_ReturnsZero_WhenNoMatch()
    {
        var (_, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "Alice", Age = 20 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = new FilterOp { Operator = "eq", Value = "99" }
        };

        var count = await repo.BulkCount(filters);
        Assert.Equal(0, count);
    }

    // ---- BulkDelete ----

    [Fact]
    public async Task BulkDelete_SoftDeletable_SetsDeletedAt()
    {
        var (db, repo) = CreateSoftRepo();
        await repo.Create(new { Name = "Alpha" });
        await repo.Create(new { Name = "Beta" });
        await repo.Create(new { Name = "Keep" });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Name"] = new FilterOp { Operator = "in", Values = ["Alpha", "Beta"] }
        };

        var affected = await repo.BulkDelete(filters);
        Assert.Equal(2, affected);

        // Detach all tracked entities so re-queries hit the database
        foreach (var entry in db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;

        // Verify soft-deleted (rows still exist with DeletedAt set)
        var deleted = await db.SoftPersons.IgnoreQueryFilters()
            .Where(e => e.DeletedAt != null).ToListAsync();
        Assert.Equal(2, deleted.Count);
        Assert.All(deleted, e => Assert.NotNull(e.DeletedAt));

        // "Keep" should remain active
        var active = await db.SoftPersons.ToListAsync();
        Assert.Single(active);
        Assert.Equal("Keep", active[0].Name);
    }

    [Fact]
    public async Task BulkDelete_NonSoftDeletable_RemovesEntities()
    {
        var (db, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "Alice", Age = 30 });
        await repo.Create(new { Name = "Bob", Age = 25 });
        await repo.Create(new { Name = "Carol", Age = 30 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = new FilterOp { Operator = "eq", Value = "30" }
        };

        var affected = await repo.BulkDelete(filters);
        Assert.Equal(2, affected);

        // Only Bob should remain
        var remaining = await db.Persons.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("Bob", remaining[0].Name);
    }

    // ---- BulkUpdate ----

    [Fact]
    public async Task BulkUpdate_UpdatesMatchingEntities()
    {
        var (db, repo) = CreatePersonRepo();
        await repo.Create(new { Name = "Alice", Age = 30 });
        await repo.Create(new { Name = "Bob", Age = 25 });
        await repo.Create(new { Name = "Carol", Age = 30 });

        var filters = new Dictionary<string, FilterOp>
        {
            ["Age"] = new FilterOp { Operator = "eq", Value = "30" }
        };
        var values = new Dictionary<string, object?>
        {
            ["Name"] = "Updated"
        };

        var affected = await repo.BulkUpdate(filters, values);
        Assert.Equal(2, affected);

        // Verify the matching entities were updated
        var updated = await db.Persons.Where(p => p.Name == "Updated").ToListAsync();
        Assert.Equal(2, updated.Count);

        // Bob should remain unchanged
        var bob = await db.Persons.FirstAsync(p => p.Name == "Bob");
        Assert.Equal(25, bob.Age);
    }
}
