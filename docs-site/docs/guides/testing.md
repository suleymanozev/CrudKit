---
sidebar_position: 1
title: Testing
---

# Testing

CrudKit provides built-in test doubles for `ICurrentUser` and supports `TimeProvider` injection for deterministic timestamps.

## FakeCurrentUser

Use `FakeCurrentUser` to simulate an authenticated user in tests:

```csharp
var user = new FakeCurrentUser
{
    Id = "user-123",
    Username = "testadmin",
    Roles = new List<string> { "admin" },
    AccessibleTenants = null  // null = all tenants (superadmin)
};
```

## AnonymousCurrentUser

`AnonymousCurrentUser` represents an unauthenticated user:

```csharp
var anon = new AnonymousCurrentUser();
// IsAuthenticated = false, no roles, no permissions
```

`AddCrudKit()` automatically registers `AnonymousCurrentUser` as the `ICurrentUser` fallback if no other implementation is found in DI.

## TestWebApp Pattern

Replace `ICurrentUser` in integration tests using `WebApplicationFactory`:

```csharp
var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder =>
    {
        builder.ConfigureServices(services =>
        {
            // Replace the real ICurrentUser with a test double
            services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser
            {
                Id = "test-user",
                Roles = ["admin"]
            });
        });
    });

var client = factory.CreateClient();
```

## TimeProvider for Deterministic Timestamps

`CrudKitDbContext` accepts an optional `TimeProvider` for testable timestamps. All `CreatedAt`, `UpdatedAt`, `DeletedAt`, and audit log `Timestamp` values use it.

```csharp
// Production — no config needed, defaults to TimeProvider.System
builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlite("Data Source=app.db"));

// Testing — inject a fake time provider
var fakeTime = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
var db = new AppDbContext(options, currentUser, fakeTime);

// Advance time between operations
fakeTime.Advance(TimeSpan.FromHours(5));
```

Constructor signature: `CrudKitDbContext(DbContextOptions, ICurrentUser, TimeProvider? timeProvider = null)`

A single timestamp is captured per `SaveChanges` call — `CreatedAt` and `UpdatedAt` on the same entity are always identical within one call.

## Integration Tests with Testcontainers

CrudKit includes provider-agnostic integration tests that run on SQLite by default and PostgreSQL via Docker automatically.

```bash
# SQLite only (no Docker needed)
dotnet test tests/CrudKit.Integration.Tests/

# SQLite + PostgreSQL (Docker required)
# PostgreSQL container starts automatically via Testcontainers
dotnet test tests/CrudKit.Integration.Tests/
```

If Docker is not available, PostgreSQL tests are skipped gracefully.

## In-Memory Database

For unit tests that don't need SQL-specific behavior, use the EF Core in-memory provider:

```csharp
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString())
    .Options;

var user = new FakeCurrentUser { Id = "test-user" };
var db = new AppDbContext(options, user);
```
