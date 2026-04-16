using CrudKit.EntityFrameworkCore.Validation;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Concurrency;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Xunit;

namespace CrudKit.Api.Tests.Validation;

public class CrudKitStartupValidatorTests
{
    [Fact]
    public void Validate_InvalidOwnerField_Throws()
    {
        var services = BuildServiceProvider<InvalidOwnerFieldDbContext>();
        var logger = services.GetRequiredService<ILogger<CrudKitStartupValidator>>();
        var validator = new CrudKitStartupValidator(services, logger);

        var ex = Assert.Throws<InvalidOperationException>(() => validator.Validate());
        Assert.Contains("OwnerField", ex.Message);
        Assert.Contains("NonExistentProperty", ex.Message);
    }

    [Fact]
    public void Validate_ConcurrentWithBulkUpdate_LogsWarning()
    {
        var services = BuildServiceProvider<ConcurrentBulkDbContext>();
        var testLogger = new TestLogger<CrudKitStartupValidator>();
        var validator = new CrudKitStartupValidator(services, testLogger);

        // Should not throw, but should log a warning
        validator.Validate();

        Assert.Contains(testLogger.Messages,
            m => m.level == LogLevel.Warning && m.message.Contains("bulk update") && m.message.Contains("concurrent"));
    }

    [Fact]
    public void Validate_MultiTenantEntitiesWithResolver_DoesNotLogWarning()
    {
        // Arrange: same DbContext but TenantResolverOptions is registered in DI
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var svc = new ServiceCollection();
        svc.AddDbContext<MultiTenantDbContext>(opts => opts.UseSqlite(connection));
        svc.AddScoped<CrudKitDbContext>(sp => sp.GetRequiredService<MultiTenantDbContext>());
        svc.AddScoped<ICurrentUser>(_ => new CrudKit.Core.Auth.AnonymousCurrentUser());
        svc.AddLogging();
        // Register a TenantResolverOptions so the validator sees it
        svc.AddSingleton(new CrudKit.Api.Tenancy.TenantResolverOptions { Resolver = _ => "test" });
        var services = svc.BuildServiceProvider();

        using (var scope = services.CreateScope())
            scope.ServiceProvider.GetRequiredService<MultiTenantDbContext>().Database.EnsureCreated();

        var testLogger = new TestLogger<CrudKitStartupValidator>();
        var validator = new CrudKitStartupValidator(services, testLogger);

        // Act
        validator.Validate();

        // Assert: no warning about missing resolver
        Assert.DoesNotContain(testLogger.Messages,
            m => m.level == LogLevel.Warning &&
                 m.message.Contains("IMultiTenant") &&
                 m.message.Contains("tenant resolver"));
    }

    // --- Helpers ---

    private static ServiceProvider BuildServiceProvider<TContext>() where TContext : CrudKitDbContext
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddDbContext<TContext>(opts => opts.UseSqlite(connection));
        services.AddScoped<CrudKitDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<ICurrentUser>(_ => new CrudKit.Core.Auth.AnonymousCurrentUser());
        services.AddLogging();
        var sp = services.BuildServiceProvider();

        // Ensure the database model is created
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreated();

        return sp;
    }
}

// --- Test DbContexts and Entities ---

[CrudEntity(OwnerField = "NonExistentProperty")]
public class BadOwnerEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class InvalidOwnerFieldDbContext : CrudKitDbContext
{
    public InvalidOwnerFieldDbContext(DbContextOptions<InvalidOwnerFieldDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<BadOwnerEntity> BadOwners => Set<BadOwnerEntity>();
}

[CrudEntity(EnableBulkUpdate = true)]
public class ConcurrentBulkEntity : IAuditableEntity, IConcurrent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ConcurrentBulkDbContext : CrudKitDbContext
{
    public ConcurrentBulkDbContext(DbContextOptions<ConcurrentBulkDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<ConcurrentBulkEntity> ConcurrentBulks => Set<ConcurrentBulkEntity>();
}

public class MultiTenantEntity : IAuditableEntity, IMultiTenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MultiTenantDbContext : CrudKitDbContext
{
    public MultiTenantDbContext(DbContextOptions<MultiTenantDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<MultiTenantEntity> MultiTenantEntities => Set<MultiTenantEntity>();
}

/// <summary>
/// Simple test logger that captures log messages for assertion.
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    public List<(LogLevel level, string message)> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Messages.Add((logLevel, formatter(state, exception)));
    }
}
