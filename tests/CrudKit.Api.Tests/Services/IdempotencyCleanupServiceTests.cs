using CrudKit.Api.Models;
using CrudKit.Api.Services;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CrudKit.Api.Tests.Services;

public class IdempotencyCleanupServiceTests : IAsyncDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly ApiTestDbContext _db;

    public IdempotencyCleanupServiceTests()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApiTestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApiTestDbContext(options, new FakeCurrentUser());
        _db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // Build an IServiceScopeFactory that resolves the test DbContext.
    private IServiceScopeFactory BuildScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddSingleton<CrudKit.EntityFrameworkCore.CrudKitDbContext>(_db);
        services.AddSingleton<CrudKit.EntityFrameworkCore.ICrudKitDbContext>(_db);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    [Fact]
    public async Task CleanupAsync_DeletesExpiredRecords_LeavesActiveOnes()
    {
        var now = DateTime.UtcNow;

        // Insert one expired record and one still-active record.
        _db.Set<IdempotencyRecord>().AddRange(
            new IdempotencyRecord
            {
                Id = Guid.NewGuid().ToString(),
                Key = "user1:expired-key",
                Path = "/items",
                Method = "POST",
                StatusCode = 200,
                CreatedAt = now.AddHours(-25),
                ExpiresAt = now.AddHours(-1) // already expired
            },
            new IdempotencyRecord
            {
                Id = Guid.NewGuid().ToString(),
                Key = "user1:active-key",
                Path = "/items",
                Method = "POST",
                StatusCode = 200,
                CreatedAt = now.AddMinutes(-10),
                ExpiresAt = now.AddHours(23) // still valid
            });
        await _db.SaveChangesAsync();

        Assert.Equal(2, await _db.Set<IdempotencyRecord>().CountAsync());

        // Invoke cleanup via the service using reflection (CleanupAsync is private).
        var scopeFactory = BuildScopeFactory();
        var logger = NullLogger<IdempotencyCleanupService>.Instance;
        var service = new IdempotencyCleanupService(scopeFactory, logger);

        var method = typeof(IdempotencyCleanupService)
            .GetMethod("CleanupAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        await (Task)method.Invoke(service, [CancellationToken.None])!;

        // Only the active record should survive.
        var remaining = await _db.Set<IdempotencyRecord>().ToListAsync();
        Assert.Single(remaining);
        Assert.Equal("user1:active-key", remaining[0].Key);
    }

    [Fact]
    public async Task CleanupAsync_WhenNoExpiredRecords_DoesNotThrow()
    {
        var now = DateTime.UtcNow;

        _db.Set<IdempotencyRecord>().Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid().ToString(),
            Key = "user1:active-key",
            Path = "/items",
            Method = "POST",
            StatusCode = 200,
            CreatedAt = now,
            ExpiresAt = now.AddHours(24)
        });
        await _db.SaveChangesAsync();

        var scopeFactory = BuildScopeFactory();
        var service = new IdempotencyCleanupService(scopeFactory, NullLogger<IdempotencyCleanupService>.Instance);

        var method = typeof(IdempotencyCleanupService)
            .GetMethod("CleanupAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        // Must not throw; count must stay at 1.
        await (Task)method.Invoke(service, [CancellationToken.None])!;

        Assert.Equal(1, await _db.Set<IdempotencyRecord>().CountAsync());
    }
}
