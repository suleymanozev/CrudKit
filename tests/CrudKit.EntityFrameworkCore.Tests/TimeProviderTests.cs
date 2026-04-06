using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests;

public class TimeProviderTests
{
    /// <summary>
    /// FakeTimeProvider allows tests to control the current UTC time and advance it manually.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset startTime)
        {
            _utcNow = startTime;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    // ---- Test 1: SaveChanges uses injected TimeProvider for CreatedAt/UpdatedAt on Add ----

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForCreatedAtAndUpdatedAt_OnAdd()
    {
        var fixedTime = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);

        using var db = DbHelper.CreateDb(timeProvider: fakeTime);
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        Assert.Equal(fixedTime.UtcDateTime, person.CreatedAt);
        Assert.Equal(fixedTime.UtcDateTime, person.UpdatedAt);
    }

    // ---- Test 2: SaveChanges uses injected TimeProvider for UpdatedAt on Modify ----

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForUpdatedAt_OnModify()
    {
        var startTime = new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(startTime);

        using var db = DbHelper.CreateDb(timeProvider: fakeTime);
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var createdAt = person.CreatedAt;

        // Advance time before update
        fakeTime.Advance(TimeSpan.FromHours(1));
        var advancedTime = fakeTime.GetUtcNow().UtcDateTime;

        person.Name = "Bob";
        await db.SaveChangesAsync();

        // CreatedAt must remain unchanged
        Assert.Equal(createdAt, person.CreatedAt);
        // UpdatedAt must reflect the advanced time
        Assert.Equal(advancedTime, person.UpdatedAt);
        Assert.NotEqual(createdAt, person.UpdatedAt);
    }

    // ---- Test 3: SaveChanges uses injected TimeProvider for soft-delete DeletedAt ----

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForDeletedAt_OnSoftDelete()
    {
        var startTime = new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(startTime);

        using var db = DbHelper.CreateDb(timeProvider: fakeTime);
        var entity = new SoftPersonEntity { Name = "Alice" };
        db.SoftPersons.Add(entity);
        await db.SaveChangesAsync();

        // Advance time before delete
        fakeTime.Advance(TimeSpan.FromDays(1));
        var deleteTime = fakeTime.GetUtcNow().UtcDateTime;

        db.SoftPersons.Remove(entity);
        await db.SaveChangesAsync();

        // Verify row still exists and DeletedAt is the controlled time
        var raw = await db.SoftPersons.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == entity.Id);
        Assert.NotNull(raw);
        Assert.Equal(deleteTime, raw!.DeletedAt);
        Assert.Equal(deleteTime, raw.UpdatedAt);
    }

    // ---- Test 4: SaveChanges uses injected TimeProvider for audit log Timestamp ----

    [Fact]
    public async Task SaveChanges_UsesInjectedTimeProvider_ForAuditLogTimestamp()
    {
        var fixedTime = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);

        using var db = DbHelper.CreateDb(timeProvider: fakeTime, auditTrailEnabled: true);
        var entity = new AuditPersonEntity { Name = "Alice" };
        db.AuditPersons.Add(entity);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(fixedTime.UtcDateTime, log!.Timestamp);
    }

    // ---- Test 5: Defaults to system time when no TimeProvider injected ----

    [Fact]
    public async Task SaveChanges_DefaultsToSystemTime_WhenNoTimeProviderInjected()
    {
        var before = DateTime.UtcNow;

        // No TimeProvider passed — should use TimeProvider.System internally
        using var db = DbHelper.CreateDb();
        var person = new PersonEntity { Name = "Alice" };
        db.Persons.Add(person);
        await db.SaveChangesAsync();

        var after = DateTime.UtcNow;

        Assert.True(person.CreatedAt >= before,
            $"CreatedAt {person.CreatedAt:O} should be >= {before:O}");
        Assert.True(person.CreatedAt <= after,
            $"CreatedAt {person.CreatedAt:O} should be <= {after:O}");
    }
}
