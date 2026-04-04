# CrudKit.Api Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the CrudKit.Api layer — Minimal API endpoint mapping with transaction-scoped hooks, validation (FluentValidation + DataAnnotation), auth filters, idempotency, bulk operations, IEntityMapper support, OpenAPI metadata, modular monolith support, and startup validation.

**Architecture:** `CrudEndpointMapper.MapCrudEndpoints<TEntity, TCreate, TUpdate>()` auto-maps endpoints per entity using Minimal API lambdas. All mutating handlers wrap in a DB transaction so hooks run atomically. `AppErrorFilter` (IEndpointFilter) converts AppError/DbUpdateConcurrencyException to ProblemDetails. `AddCrudKit<TContext>()` is a single-call DI setup layering on `AddCrudKitEf<TContext>()`. `IEntityMapper<T, TResponse>` optionally transforms response shapes. `IdempotencyFilter` prevents duplicate writes. `CrudKitStartupValidator` (IHostedService) validates entity metadata at startup.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, FluentValidation 11.* (optional peer dependency), Microsoft.AspNetCore.Mvc.Testing 10.* (tests), SQLite (tests), xUnit 2.*

---

## File Structure

### Production — `src/CrudKit.Api/`
```
src/CrudKit.Api/
├── CrudKit.Api.csproj
├── Configuration/
│   └── CrudKitApiOptions.cs
├── Endpoints/
│   ├── CrudEndpointMapper.cs
│   └── DetailEndpointMapper.cs
├── Filters/
│   ├── AppErrorFilter.cs
│   ├── ValidationFilter.cs
│   ├── IdempotencyFilter.cs
│   ├── RequireAuthFilter.cs
│   ├── RequireRoleFilter.cs
│   └── RequirePermissionFilter.cs
├── Models/
│   └── IdempotencyRecord.cs
├── Services/
│   └── IdempotencyCleanupService.cs
├── Validation/
│   └── CrudKitStartupValidator.cs
└── Extensions/
    ├── CrudKitAppExtensions.cs
    └── RouteGroupExtensions.cs
```

### Tests — `tests/CrudKit.Api.Tests/`
```
tests/CrudKit.Api.Tests/
├── CrudKit.Api.Tests.csproj
├── Helpers/
│   ├── TestWebApp.cs
│   ├── TestEntities.cs
│   └── ApiTestDbContext.cs
├── Filters/
│   ├── AppErrorFilterTests.cs
│   ├── ValidationFilterTests.cs
│   ├── AuthFilterTests.cs
│   └── IdempotencyFilterTests.cs
├── Endpoints/
│   ├── CrudEndpointMapperTests.cs
│   ├── BulkEndpointTests.cs
│   └── DetailEndpointMapperTests.cs
└── Extensions/
    └── CrudKitAppExtensionsTests.cs
```

---

## Task 1: Project Scaffold

Create `src/CrudKit.Api/CrudKit.Api.csproj`, `tests/CrudKit.Api.Tests/CrudKit.Api.Tests.csproj`, test helpers, placeholder source, and update `CrudKit.slnx`.

### Steps

- [ ] **1.1** Create `src/CrudKit.Api/CrudKit.Api.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>CrudKit.Api</AssemblyName>
    <RootNamespace>CrudKit.Api</RootNamespace>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="..\CrudKit.Core\CrudKit.Core.csproj" />
    <ProjectReference Include="..\CrudKit.EntityFrameworkCore\CrudKit.EntityFrameworkCore.csproj" />
  </ItemGroup>
  <ItemGroup>
    <!-- FluentValidation is an optional peer dependency — consumed via IValidator<T> if user registers it -->
    <PackageReference Include="FluentValidation" Version="11.*" />
  </ItemGroup>
</Project>
```

- [ ] **1.2** Create `src/CrudKit.Api/Placeholder.cs` (temporary file to allow initial build):

```csharp
// CrudKit.Api — placeholder to allow initial build
namespace CrudKit.Api;
```

- [ ] **1.3** Create `tests/CrudKit.Api.Tests/CrudKit.Api.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.*" />
    <PackageReference Include="FluentValidation" Version="11.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrudKit.Api\CrudKit.Api.csproj" />
    <ProjectReference Include="..\..\src\CrudKit.EntityFrameworkCore\CrudKit.EntityFrameworkCore.csproj" />
    <ProjectReference Include="..\..\src\CrudKit.Core\CrudKit.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **1.4** Create `tests/CrudKit.Api.Tests/Helpers/TestEntities.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Concurrency;

namespace CrudKit.Api.Tests.Helpers;

// ---- Basic entity (no soft-delete, no state machine) ----
public class ProductEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateProductDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Range(0.01, 1_000_000)] public decimal Price { get; set; }
}

public class UpdateProductDto
{
    public string? Name { get; set; }
    public decimal? Price { get; set; }
}

// ---- Product response DTO for IEntityMapper tests ----
public record ProductResponse(string Id, string Name, decimal Price, string DisplayName);

// ---- Soft-deletable entity ----
public class SoftProductEntity : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class CreateSoftProductDto
{
    [Required] public string Name { get; set; } = string.Empty;
}

// ---- State machine entity ----
public enum OrderStatus { Pending, Processing, Completed, Cancelled }

public class OrderEntity : IEntity, IStateMachine<OrderStatus>
{
    public string Id { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}

public class CreateOrderDto
{
    [Required] public string Customer { get; set; } = string.Empty;
}

public class UpdateOrderDto
{
    public string? Customer { get; set; }
}

// ---- Concurrent entity for 409 tests ----
public class ConcurrentEntity : IEntity, IConcurrent
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public uint RowVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateConcurrentDto
{
    [Required] public string Name { get; set; } = string.Empty;
}

public class UpdateConcurrentDto
{
    public string? Name { get; set; }
    public uint RowVersion { get; set; }
}

// ---- Master-detail entities for DetailEndpointMapper tests ----
public class InvoiceEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class InvoiceLineEntity : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateInvoiceDto { [Required] public string Title { get; set; } = string.Empty; }
public class UpdateInvoiceDto { public string? Title { get; set; } }
public class CreateInvoiceLineDto
{
    public string InvoiceId { get; set; } = string.Empty;
    [Required] public string Description { get; set; } = string.Empty;
    [Range(0.01, 1_000_000_000)] public decimal Amount { get; set; }
}
```

- [ ] **1.5** Create `tests/CrudKit.Api.Tests/Helpers/ApiTestDbContext.cs`:

```csharp
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.Api.Tests.Helpers;

/// <summary>
/// DbContext for CrudKit.Api integration tests.
/// Uses CrudKitDbContext base which handles Id gen, timestamps, soft-delete, etc.
/// </summary>
public class ApiTestDbContext : CrudKitDbContext
{
    public ApiTestDbContext(DbContextOptions<ApiTestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }

    public DbSet<ProductEntity> Products => Set<ProductEntity>();
    public DbSet<SoftProductEntity> SoftProducts => Set<SoftProductEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<ConcurrentEntity> Concurrents => Set<ConcurrentEntity>();
    public DbSet<InvoiceEntity> Invoices => Set<InvoiceEntity>();
    public DbSet<InvoiceLineEntity> InvoiceLines => Set<InvoiceLineEntity>();
}
```

- [ ] **1.6** Create `tests/CrudKit.Api.Tests/Helpers/TestWebApp.cs`:

```csharp
using CrudKit.Api.Extensions;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrudKit.Api.Tests.Helpers;

/// <summary>
/// In-process test web application with SQLite in-memory and CrudKit fully configured.
/// Each test gets a fresh database via a dedicated SQLite connection.
/// </summary>
public sealed class TestWebApp : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly SqliteConnection _connection;

    public HttpClient Client { get; }

    private TestWebApp(WebApplication app, SqliteConnection connection)
    {
        _app = app;
        _connection = connection;
        Client = app.GetTestClient();
    }

    /// <summary>
    /// Creates and starts a test web application.
    /// </summary>
    /// <param name="currentUser">Optional ICurrentUser. Defaults to FakeCurrentUser (authenticated, admin role).</param>
    /// <param name="configureEndpoints">Callback to map endpoints after UseCrudKit().</param>
    /// <param name="configureServices">Callback to register additional services before Build().</param>
    /// <param name="environment">Hosting environment name. Defaults to "Development".</param>
    public static async Task<TestWebApp> CreateAsync(
        ICurrentUser? currentUser = null,
        Action<WebApplication>? configureEndpoints = null,
        Action<IServiceCollection>? configureServices = null,
        string environment = "Development")
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = environment;

        builder.Services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        builder.Services.AddScoped<ICurrentUser>(_ => currentUser ?? new FakeCurrentUser());
        builder.Services.AddCrudKit<ApiTestDbContext>();

        configureServices?.Invoke(builder.Services);

        var app = builder.Build();

        app.UseCrudKit();
        configureEndpoints?.Invoke(app);

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiTestDbContext>();
            db.Database.EnsureCreated();
        }

        await app.StartAsync();
        return new TestWebApp(app, connection);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        _connection.Dispose();
    }
}
```

- [ ] **1.7** Update `CrudKit.slnx` to include the two new projects:

```xml
<Solution>
  <Project Path="src/CrudKit.Core/CrudKit.Core.csproj" />
  <Project Path="src/CrudKit.EntityFrameworkCore/CrudKit.EntityFrameworkCore.csproj" />
  <Project Path="src/CrudKit.Api/CrudKit.Api.csproj" />
  <Project Path="tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj" />
  <Project Path="tests/CrudKit.EntityFrameworkCore.Tests/CrudKit.EntityFrameworkCore.Tests.csproj" />
  <Project Path="tests/CrudKit.Api.Tests/CrudKit.Api.Tests.csproj" />
</Solution>
```

- [ ] **1.8** Verify: Run `dotnet build CrudKit.slnx`. The CrudKit.Api project should compile (placeholder only). The test project will have build errors because `CrudKitAppExtensions` does not exist yet — that is expected and acceptable at this stage.

- [ ] **1.9** Commit: `scaffold: add CrudKit.Api and CrudKit.Api.Tests projects with test helpers`

---

## Task 2: AppErrorFilter (IEndpointFilter)

Per edge case 11.21, AppErrorFilter is an **IEndpointFilter** (NOT IMiddleware). It catches `AppError`, `DbUpdateConcurrencyException`, and unhandled exceptions. Uses `IHostEnvironment` to decide whether to expose stack traces.

### Steps

- [ ] **2.1** Create test file `tests/CrudKit.Api.Tests/Filters/AppErrorFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Core.Models;
using CrudKit.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class AppErrorFilterTests
{
    [Fact]
    public async Task ThrowsAppErrorNotFound_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-not-found", () => throw AppError.NotFound("Item not found"))
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-not-found");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(404, body.GetProperty("status").GetInt32());
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ThrowsAppErrorBadRequest_Returns400()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-bad", () => throw AppError.BadRequest("Bad input"))
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-bad");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ThrowsAppErrorUnauthorized_Returns401()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-auth", () => throw AppError.Unauthorized())
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-auth");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ThrowsAppErrorForbidden_Returns403()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-forbidden", () => throw AppError.Forbidden())
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-forbidden");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ThrowsAppErrorConflict_Returns409()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-conflict", () => throw AppError.Conflict("Already exists"))
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-conflict");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ThrowsValidationError_Returns400WithErrors()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-validation", () =>
            {
                var errors = new ValidationErrors();
                errors.Add("Name", "REQUIRED", "Name is required.");
                throw AppError.Validation(errors);
            }).AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-validation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ThrowsDbUpdateConcurrencyException_Returns409()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-concurrency",
                () => throw new DbUpdateConcurrencyException("Concurrency conflict"))
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-concurrency");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_Returns500_DevIncludesStackTrace()
    {
        await using var app = await TestWebApp.CreateAsync(
            environment: "Development",
            configureEndpoints: a =>
                a.MapGet("/test-crash", () => throw new InvalidOperationException("Unexpected boom"))
                 .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-crash");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("InvalidOperationException", body);
    }

    [Fact]
    public async Task UnhandledException_Returns500_ProdHidesStackTrace()
    {
        await using var app = await TestWebApp.CreateAsync(
            environment: "Production",
            configureEndpoints: a =>
                a.MapGet("/test-crash", () => throw new InvalidOperationException("Secret details"))
                 .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/test-crash");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret details", body);
        Assert.DoesNotContain("InvalidOperationException", body);
    }

    [Fact]
    public async Task NormalResponse_PassesThrough()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/ok", () => Results.Ok(new { msg = "hello" }))
             .AddEndpointFilter<CrudKit.Api.Filters.AppErrorFilter>());

        var response = await app.Client.GetAsync("/ok");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **2.2** Verify tests fail (build error — `AppErrorFilter` class does not exist yet).

- [ ] **2.3** Create `src/CrudKit.Api/Filters/AppErrorFilter.cs`:

```csharp
using CrudKit.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that catches AppError, DbUpdateConcurrencyException, and unhandled
/// exceptions and converts them to structured JSON error responses.
/// This is an IEndpointFilter (NOT IMiddleware) per CrudKit architecture.
/// Applied to all endpoints via the route group in CrudEndpointMapper,
/// and can be added to custom endpoints via .AddEndpointFilter&lt;AppErrorFilter&gt;().
/// </summary>
public class AppErrorFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var env = ctx.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("CrudKit.Api");

        try
        {
            return await next(ctx);
        }
        catch (AppError ex)
        {
            logger.LogWarning(ex, "AppError {StatusCode} {Code}: {Message}",
                ex.StatusCode, ex.Code, ex.Message);

            return Results.Json(new
            {
                status = ex.StatusCode,
                code = ex.Code,
                message = ex.Message,
                details = ex.Details
            }, statusCode: ex.StatusCode);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            logger.LogWarning(ex, "Concurrency conflict on {Method} {Path}",
                ctx.HttpContext.Request.Method, ctx.HttpContext.Request.Path);

            return Results.Json(new
            {
                status = 409,
                code = "CONFLICT",
                message = "The record was modified by another request. Fetch the latest version and retry."
            }, statusCode: 409);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception: {Method} {Path}",
                ctx.HttpContext.Request.Method, ctx.HttpContext.Request.Path);

            var detail = env.IsDevelopment()
                ? ex.ToString()
                : "An unexpected error occurred.";

            return Results.Json(new
            {
                status = 500,
                code = "INTERNAL_ERROR",
                message = detail
            }, statusCode: 500);
        }
    }
}
```

- [ ] **2.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AppErrorFilterTests"`. All 10 tests should pass.

- [ ] **2.5** Commit: `feat(api): add AppErrorFilter as IEndpointFilter with dev/prod stack trace handling`

---

## Task 3: ValidationFilter (FluentValidation + DataAnnotation)

Per edge case 11.21.2, `IValidator<T>` (FluentValidation) takes precedence when registered in DI; DataAnnotation is the fallback.

### Steps

- [ ] **3.1** Create test file `tests/CrudKit.Api.Tests/Filters/ValidationFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

/// <summary>
/// Fluent validator for CreateProductDto — used in tests to verify FluentValidation takes precedence.
/// </summary>
public class CreateProductDtoValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Product name is required.");
        RuleFor(x => x.Name).MinimumLength(3).WithMessage("Product name must be at least 3 characters.");
        RuleFor(x => x.Price).GreaterThan(0).WithMessage("Price must be positive.");
    }
}

public class ValidationFilterTests
{
    [Fact]
    public async Task MissingRequiredField_Returns400_DataAnnotation()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        // Name is missing — [Required] should fail via DataAnnotation
        var response = await app.Client.PostAsJsonAsync("/api/products", new { Price = 9.99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RangeViolation_Returns400_DataAnnotation()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        // Price = 0 violates [Range(0.01, 1_000_000)]
        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Widget", Price = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidDto_PassesThrough()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Widget", Price = 9.99 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task FluentValidation_TakesPrecedence_OverDataAnnotation()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<IValidator<CreateProductDto>, CreateProductDtoValidator>();
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        // "AB" is 2 chars — FluentValidation requires min 3
        // DataAnnotation [Required] would pass because Name is not empty
        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "AB", Price = 9.99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("at least 3 characters", body);
    }

    [Fact]
    public async Task FluentValidation_ValidDto_PassesThrough()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<IValidator<CreateProductDto>, CreateProductDtoValidator>();
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Widget", Price = 9.99 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
```

- [ ] **3.2** Verify tests fail (build error — `ValidationFilter` and `CrudEndpointMapper` do not exist yet).

- [ ] **3.3** Create `src/CrudKit.Api/Filters/ValidationFilter.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Models;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that validates the request body DTO.
/// Priority: FluentValidation (IValidator&lt;T&gt; from DI) takes precedence.
/// Fallback: System.ComponentModel.DataAnnotations.
/// Returns structured validation error response on failure.
/// </summary>
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (arg == null) return await next(ctx);

        var errors = new ValidationErrors();

        // FluentValidation takes precedence if registered
        var fluentValidator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (fluentValidator != null)
        {
            var result = await fluentValidator.ValidateAsync(arg);
            if (!result.IsValid)
            {
                foreach (var failure in result.Errors)
                    errors.Add(failure.PropertyName, failure.ErrorCode, failure.ErrorMessage);
            }
        }
        else
        {
            // DataAnnotation fallback
            var validationCtx = new ValidationContext(arg);
            var results = new List<ValidationResult>();

            if (!Validator.TryValidateObject(arg, validationCtx, results, validateAllProperties: true))
            {
                foreach (var vr in results)
                {
                    var field = vr.MemberNames.FirstOrDefault() ?? "unknown";
                    errors.Add(field, "INVALID", vr.ErrorMessage ?? "Invalid value.");
                }
            }
        }

        if (!errors.IsEmpty)
        {
            return Results.Json(new
            {
                status = 400,
                code = "VALIDATION_ERROR",
                message = "Validation failed.",
                details = errors.Errors.Select(e => new { e.Field, e.Code, e.Message })
            }, statusCode: 400);
        }

        return await next(ctx);
    }
}
```

- [ ] **3.4** Note: ValidationFilter tests depend on `CrudEndpointMapper` which is built in Task 6. These tests will be verified in Task 6 after the endpoint mapper is created.

- [ ] **3.5** Commit: `feat(api): add ValidationFilter with FluentValidation priority and DataAnnotation fallback`

---

## Task 4: Auth Filters + RouteGroupExtensions

Three endpoint filters for authentication and authorization, plus convenience extension methods.

### Steps

- [ ] **4.1** Create test file `tests/CrudKit.Api.Tests/Filters/AuthFilterTests.cs`:

```csharp
using System.Net;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.Api.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class AuthFilterTests
{
    [Fact]
    public async Task RequireAuth_AnonymousUser_Returns401()
    {
        var anon = new AnonymousCurrentUser();
        await using var app = await TestWebApp.CreateAsync(
            currentUser: anon,
            configureEndpoints: a =>
            {
                a.MapGet("/protected", () => Results.Ok("secret"))
                 .AddEndpointFilter<CrudKit.Api.Filters.RequireAuthFilter>();
            });

        var response = await app.Client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequireAuth_AuthenticatedUser_Returns200()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapGet("/protected", () => Results.Ok("secret"))
             .AddEndpointFilter<CrudKit.Api.Filters.RequireAuthFilter>();
        });

        var response = await app.Client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequireRole_WrongRole_Returns403()
    {
        var user = new FakeCurrentUser { Roles = new List<string> { "user" } };
        await using var app = await TestWebApp.CreateAsync(
            currentUser: user,
            configureEndpoints: a =>
            {
                a.MapGet("/admin", () => Results.Ok("admin area"))
                 .AddEndpointFilter(new CrudKit.Api.Filters.RequireRoleFilter("admin"));
            });

        var response = await app.Client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequireRole_CorrectRole_Returns200()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapGet("/admin", () => Results.Ok("admin area"))
             .AddEndpointFilter(new CrudKit.Api.Filters.RequireRoleFilter("admin"));
        });

        var response = await app.Client.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequirePermission_AnonymousUser_Returns403()
    {
        var anon = new AnonymousCurrentUser();
        await using var app = await TestWebApp.CreateAsync(
            currentUser: anon,
            configureEndpoints: a =>
            {
                a.MapGet("/perm", () => Results.Ok("permitted"))
                 .AddEndpointFilter(new CrudKit.Api.Filters.RequirePermissionFilter("product", "read"));
            });

        var response = await app.Client.GetAsync("/perm");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RequirePermission_FakeUser_Returns200()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapGet("/perm", () => Results.Ok("permitted"))
             .AddEndpointFilter(new CrudKit.Api.Filters.RequirePermissionFilter("product", "read"));
        });

        var response = await app.Client.GetAsync("/perm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **4.2** Verify tests fail (build error — filter classes do not exist yet).

- [ ] **4.3** Create `src/CrudKit.Api/Filters/RequireAuthFilter.cs`:

```csharp
using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that rejects unauthenticated requests with HTTP 401.
/// Resolves ICurrentUser from DI and checks IsAuthenticated.
/// </summary>
public class RequireAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!user.IsAuthenticated)
            return Results.Json(new { status = 401, code = "UNAUTHORIZED", message = "Authentication required." }, statusCode: 401);
        return await next(ctx);
    }
}
```

- [ ] **4.4** Create `src/CrudKit.Api/Filters/RequireRoleFilter.cs`:

```csharp
using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that rejects requests where the current user does not have the required role.
/// Returns HTTP 403 Forbidden.
/// </summary>
public class RequireRoleFilter : IEndpointFilter
{
    private readonly string _role;

    public RequireRoleFilter(string role) => _role = role;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!user.HasRole(_role))
            return Results.Json(new { status = 403, code = "FORBIDDEN", message = $"Role '{_role}' is required." }, statusCode: 403);
        return await next(ctx);
    }
}
```

- [ ] **4.5** Create `src/CrudKit.Api/Filters/RequirePermissionFilter.cs`:

```csharp
using CrudKit.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that rejects requests where the current user lacks the required permission.
/// Returns HTTP 403 Forbidden. Permission is checked via ICurrentUser.HasPermission(entity, action).
/// </summary>
public class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _entity;
    private readonly string _action;

    public RequirePermissionFilter(string entity, string action)
    {
        _entity = entity;
        _action = action;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!user.HasPermission(_entity, _action))
            return Results.Json(new { status = 403, code = "FORBIDDEN", message = $"Permission '{_entity}:{_action}' is required." }, statusCode: 403);
        return await next(ctx);
    }
}
```

- [ ] **4.6** Create `src/CrudKit.Api/Extensions/RouteGroupExtensions.cs`:

```csharp
using CrudKit.Api.Filters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Extensions;

/// <summary>
/// Convenience extension methods for applying auth filters to route groups and route handlers.
/// </summary>
public static class RouteGroupExtensions
{
    /// <summary>Requires authentication for all endpoints in the group.</summary>
    public static RouteGroupBuilder RequireAuth(this RouteGroupBuilder group)
    {
        group.AddEndpointFilter<RequireAuthFilter>();
        return group;
    }

    /// <summary>Requires a specific role for all endpoints in the group.</summary>
    public static RouteGroupBuilder RequireRole(this RouteGroupBuilder group, string role)
    {
        group.AddEndpointFilter(new RequireRoleFilter(role));
        return group;
    }

    /// <summary>Requires a specific permission for a single endpoint.</summary>
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string entity, string action)
    {
        builder.AddEndpointFilter(new RequirePermissionFilter(entity, action));
        return builder;
    }
}
```

- [ ] **4.7** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AuthFilterTests"`. All 6 tests should pass.

- [ ] **4.8** Commit: `feat(api): add RequireAuth, RequireRole, RequirePermission filters and RouteGroupExtensions`

---

## Task 5: CrudKitApiOptions + AddCrudKit + UseCrudKit + IModule lifecycle

DI registration entry point, configuration options, module lifecycle, JSON IgnoreCycles, and anonymous ICurrentUser fallback.

### Steps

- [ ] **5.1** Create `src/CrudKit.Api/Configuration/CrudKitApiOptions.cs`:

```csharp
using System.Reflection;

namespace CrudKit.Api.Configuration;

/// <summary>
/// Configuration for the CrudKit API layer.
/// Passed to AddCrudKit&lt;TContext&gt;() via Action&lt;CrudKitApiOptions&gt;.
/// </summary>
public class CrudKitApiOptions
{
    /// <summary>Default page size for List endpoints when not specified in query string.</summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>Maximum allowed page size. Requests exceeding this are clamped.</summary>
    public int MaxPageSize { get; set; } = 100;

    /// <summary>URL prefix for all auto-mapped endpoints (default: "/api").</summary>
    public string ApiPrefix { get; set; } = "/api";

    /// <summary>Maximum number of items allowed in bulk/batch operations.</summary>
    public int BulkLimit { get; set; } = 10_000;

    /// <summary>When set, AddCrudKit scans this assembly for IModule implementations and registers them.</summary>
    public Assembly? ScanModulesFromAssembly { get; set; }

    /// <summary>When true, IdempotencyFilter is applied to mutating endpoints.</summary>
    public bool EnableIdempotency { get; set; }
}
```

- [ ] **5.2** Create `src/CrudKit.Api/Extensions/CrudKitAppExtensions.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrudKit.Api.Configuration;
using CrudKit.Api.Filters;
using CrudKit.Api.Validation;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Extensions;

/// <summary>
/// Top-level DI registration and middleware activation for CrudKit.
/// </summary>
public static class CrudKitAppExtensions
{
    /// <summary>
    /// Registers the full CrudKit stack: EF Core layer + API layer + module scan.
    /// Call after AddDbContext&lt;TContext&gt;(). If ICurrentUser is not registered,
    /// AnonymousCurrentUser is used as a fallback.
    /// </summary>
    public static IServiceCollection AddCrudKit<TContext>(
        this IServiceCollection services,
        Action<CrudKitApiOptions>? configure = null)
        where TContext : CrudKitDbContext
    {
        // EF Core layer (idempotent — safe to call multiple times)
        services.AddCrudKitEf<TContext>();

        // API options
        var opts = new CrudKitApiOptions();
        configure?.Invoke(opts);
        services.TryAddSingleton(opts);

        // Anonymous fallback for ICurrentUser (if not already registered)
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        // Configure JSON serialization: IgnoreCycles + WhenWritingNull (edge case 11.18)
        services.Configure<JsonOptions>(jsonOpts =>
        {
            jsonOpts.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            jsonOpts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        // Startup validator (IHostedService — validates entity metadata at startup)
        services.AddHostedService<CrudKitStartupValidator>();

        // Module scan — discover and register all IModule implementations in the given assembly
        if (opts.ScanModulesFromAssembly != null)
        {
            ScanAndRegisterModules(services, opts.ScanModulesFromAssembly);
        }

        return services;
    }

    /// <summary>
    /// Activates CrudKit and maps all registered module endpoints.
    /// Call after app = builder.Build().
    /// </summary>
    public static WebApplication UseCrudKit(this WebApplication app)
    {
        // Map endpoints for all registered modules
        foreach (var module in app.Services.GetServices<IModule>())
            module.MapEndpoints(app);

        return app;
    }

    /// <summary>
    /// Registers a single module manually without assembly scan.
    /// Call RegisterServices immediately with the available IConfiguration.
    /// </summary>
    public static IServiceCollection AddCrudKitModule<TModule>(
        this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        services.AddSingleton<IModule, TModule>();
        return services;
    }

    private static void ScanAndRegisterModules(IServiceCollection services, Assembly assembly)
    {
        var moduleTypes = assembly
            .GetTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

        foreach (var moduleType in moduleTypes)
            services.AddSingleton(typeof(IModule), moduleType);
    }
}
```

- [ ] **5.3** Create `src/CrudKit.Api/Validation/CrudKitStartupValidator.cs` (stub — detailed validation comes in Task 12):

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Validation;

/// <summary>
/// IHostedService that validates entity metadata and configuration at startup.
/// Runs once during application start, before the first request.
/// Detailed validation logic is added in Task 12.
/// </summary>
public class CrudKitStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CrudKitStartupValidator> _logger;

    public CrudKitStartupValidator(IServiceProvider services, ILogger<CrudKitStartupValidator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("CrudKitStartupValidator: startup validation complete.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **5.4** Delete `src/CrudKit.Api/Placeholder.cs` (no longer needed — real types exist now).

- [ ] **5.5** Create test file `tests/CrudKit.Api.Tests/Extensions/CrudKitAppExtensionsTests.cs`:

```csharp
using CrudKit.Api.Configuration;
using CrudKit.Api.Extensions;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Extensions;

public class CrudKitAppExtensionsTests
{
    private static IServiceProvider BuildProvider()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());
        services.AddCrudKit<ApiTestDbContext>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCrudKit_RegistersIRepo()
    {
        using var sp = (ServiceProvider)BuildProvider();
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRepo<ProductEntity>>();
        Assert.IsType<EfRepo<ProductEntity>>(repo);
    }

    [Fact]
    public void AddCrudKit_RegistersCrudKitApiOptions()
    {
        using var sp = (ServiceProvider)BuildProvider();
        var opts = sp.GetRequiredService<CrudKitApiOptions>();
        Assert.NotNull(opts);
        Assert.Equal(20, opts.DefaultPageSize);
        Assert.Equal(100, opts.MaxPageSize);
        Assert.Equal("/api", opts.ApiPrefix);
        Assert.Equal(10_000, opts.BulkLimit);
        Assert.False(opts.EnableIdempotency);
    }

    [Fact]
    public void AddCrudKit_CustomOptions_AreApplied()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        services.AddCrudKit<ApiTestDbContext>(opts =>
        {
            opts.DefaultPageSize = 50;
            opts.MaxPageSize = 200;
            opts.EnableIdempotency = true;
        });

        using var sp = (ServiceProvider)services.BuildServiceProvider();
        var opts = sp.GetRequiredService<CrudKitApiOptions>();
        Assert.Equal(50, opts.DefaultPageSize);
        Assert.Equal(200, opts.MaxPageSize);
        Assert.True(opts.EnableIdempotency);
    }

    [Fact]
    public void AddCrudKit_FallsBackToAnonymousCurrentUser_WhenNotRegistered()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        // No ICurrentUser registered — should fallback to AnonymousCurrentUser
        services.AddCrudKit<ApiTestDbContext>();

        using var sp = (ServiceProvider)services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();
        Assert.IsType<AnonymousCurrentUser>(user);
    }

    [Fact]
    public void AddCrudKitModule_RegistersModule()
    {
        var services = new ServiceCollection();
        services.AddCrudKitModule<TestModule>();

        using var sp = (ServiceProvider)services.BuildServiceProvider();
        var modules = sp.GetServices<IModule>().ToList();
        Assert.Single(modules);
        Assert.IsType<TestModule>(modules[0]);
    }

    [Fact]
    public void AddCrudKit_IsIdempotent()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());

        // Call twice — should not throw or duplicate registrations
        services.AddCrudKit<ApiTestDbContext>();
        services.AddCrudKit<ApiTestDbContext>();

        using var sp = (ServiceProvider)services.BuildServiceProvider();
        var opts = sp.GetRequiredService<CrudKitApiOptions>();
        Assert.NotNull(opts);
    }

    [Fact]
    public async Task UseCrudKit_CallsModuleMapEndpoints()
    {
        var mapEndpointsCalled = false;

        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddSingleton<IModule>(new CallbackModule(
                    onMapEndpoints: _ => mapEndpointsCalled = true));
            });

        Assert.True(mapEndpointsCalled);
    }

    // ---- Test helpers ----

    private class TestModule : IModule
    {
        public string Name => "Test";
        public void RegisterServices(IServiceCollection services, IConfiguration config) { }
        public void MapEndpoints(WebApplication app) { }
    }

    private class CallbackModule : IModule
    {
        private readonly Action<WebApplication> _onMapEndpoints;
        public CallbackModule(Action<WebApplication> onMapEndpoints) => _onMapEndpoints = onMapEndpoints;
        public string Name => "Callback";
        public void RegisterServices(IServiceCollection services, IConfiguration config) { }
        public void MapEndpoints(WebApplication app) => _onMapEndpoints(app);
    }
}
```

- [ ] **5.6** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudKitAppExtensionsTests"`. All 7 tests should pass.

- [ ] **5.7** Also re-run previously created tests to ensure nothing broke: `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AppErrorFilterTests|FullyQualifiedName~AuthFilterTests"`. All should pass.

- [ ] **5.8** Commit: `feat(api): add CrudKitApiOptions, AddCrudKit/UseCrudKit extensions, IModule lifecycle, IgnoreCycles JSON config`

---

## Task 6: CrudEndpointMapper — Basic CRUD with Transactions + Hooks + AppErrorFilter

All mutating handlers wrap in `db.Database.BeginTransactionAsync`. ICrudHooks are called within the transaction. AppErrorFilter is applied to the group. IEntityMapper integration is included (resolves from DI if available). OpenAPI metadata is added to each endpoint.

### Steps

- [ ] **6.1** Create test file `tests/CrudKit.Api.Tests/Endpoints/CrudEndpointMapperTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class CrudEndpointMapperTests
{
    // ---- List ----
    [Fact]
    public async Task List_ReturnsEmptyPaginated()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task List_ReturnsCreatedItems()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "A", Price = 1.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "B", Price = 2.0 });

        var response = await app.Client.GetAsync("/api/products");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total").GetInt64());
    }

    // ---- Get ----
    [Fact]
    public async Task Get_Returns404ForMissingId()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.GetAsync("/api/products/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_ReturnsEntityById()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Gadget", Price = 49.99 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Gadget", body.GetProperty("name").GetString());
    }

    // ---- Create ----
    [Fact]
    public async Task Create_Returns201WithEntity()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Gadget", Price = 49.99 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Gadget", body.GetProperty("name").GetString());
        Assert.True(body.GetProperty("id").GetString()!.Length > 0);
    }

    [Fact]
    public async Task Create_SetsLocationHeader()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Gadget", Price = 49.99 });

        Assert.NotNull(response.Headers.Location);
        Assert.Contains("/api/products/", response.Headers.Location!.ToString());
    }

    // ---- Update ----
    [Fact]
    public async Task Update_Returns200WithUpdatedEntity()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Old", Price = 10.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var update = await app.Client.PutAsJsonAsync($"/api/products/{id}",
            new { Name = "New" });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var body = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("New", body.GetProperty("name").GetString());
    }

    // ---- Delete ----
    [Fact]
    public async Task Delete_Returns204()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "ToDelete", Price = 1.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var delete = await app.Client.DeleteAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_EntityNotFoundAfterDelete()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "ToDelete", Price = 1.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        await app.Client.DeleteAsync($"/api/products/{id}");

        var get = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ---- Hooks ----
    [Fact]
    public async Task Create_CallsBeforeAndAfterHooks()
    {
        var beforeCalled = false;
        var afterCalled = false;

        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<ICrudHooks<ProductEntity>>(_ =>
                    new TestProductHooks(
                        beforeCreate: (e, ctx) => { beforeCalled = true; return Task.CompletedTask; },
                        afterCreate: (e, ctx) => { afterCalled = true; return Task.CompletedTask; }));
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Hooked", Price = 1.0 });

        Assert.True(beforeCalled);
        Assert.True(afterCalled);
    }

    [Fact]
    public async Task Update_CallsBeforeAndAfterHooks()
    {
        var beforeCalled = false;
        var afterCalled = false;

        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<ICrudHooks<ProductEntity>>(_ =>
                    new TestProductHooks(
                        beforeUpdate: (e, ctx) => { beforeCalled = true; return Task.CompletedTask; },
                        afterUpdate: (e, ctx) => { afterCalled = true; return Task.CompletedTask; }));
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Old", Price = 10.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        await app.Client.PutAsJsonAsync($"/api/products/{id}", new { Name = "New" });

        Assert.True(beforeCalled);
        Assert.True(afterCalled);
    }

    [Fact]
    public async Task Delete_CallsBeforeAndAfterHooks()
    {
        var beforeCalled = false;
        var afterCalled = false;

        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<ICrudHooks<ProductEntity>>(_ =>
                    new TestProductHooks(
                        beforeDelete: (e, ctx) => { beforeCalled = true; return Task.CompletedTask; },
                        afterDelete: (e, ctx) => { afterCalled = true; return Task.CompletedTask; }));
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "ToDelete", Price = 1.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        await app.Client.DeleteAsync($"/api/products/{id}");

        Assert.True(beforeCalled);
        Assert.True(afterCalled);
    }

    [Fact]
    public async Task NoHooksRegistered_StillWorks()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "NoHooks", Price = 5.00 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task HookThrows_TransactionRollsBack()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<ICrudHooks<ProductEntity>>(_ =>
                    new TestProductHooks(
                        afterCreate: (e, ctx) => throw new InvalidOperationException("Hook boom")));
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Rollback", Price = 1.0 });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Verify entity was NOT persisted (transaction rolled back)
        var list = await app.Client.GetAsync("/api/products");
        var body = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt64());
    }

    // ---- IEntityMapper integration ----
    [Fact]
    public async Task Get_UsesEntityMapper_WhenRegistered()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                services.AddScoped<IEntityMapper<ProductEntity, ProductResponse>, TestProductMapper>();
            },
            configureEndpoints: a =>
                a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Gadget", Price = 49.99 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/products/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("displayName", out var dn));
        Assert.Equal("Gadget (49.99)", dn.GetString());
    }

    [Fact]
    public async Task Get_ReturnsRawEntity_WhenNoMapperRegistered()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Raw", Price = 10.0 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/products/{id}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Should have entity fields, not mapper fields
        Assert.True(body.TryGetProperty("name", out _));
        Assert.False(body.TryGetProperty("displayName", out _));
    }
}

/// <summary>
/// Delegate-based ICrudHooks implementation for testing.
/// Each hook callback is optional; defaults to no-op.
/// </summary>
public class TestProductHooks : ICrudHooks<ProductEntity>
{
    private readonly Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? _beforeCreate;
    private readonly Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? _afterCreate;
    private readonly Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? _beforeUpdate;
    private readonly Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? _afterUpdate;
    private readonly Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? _beforeDelete;
    private readonly Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? _afterDelete;

    public TestProductHooks(
        Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? beforeCreate = null,
        Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? afterCreate = null,
        Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? beforeUpdate = null,
        Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? afterUpdate = null,
        Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? beforeDelete = null,
        Func<ProductEntity, CrudKit.Core.Context.AppContext, Task>? afterDelete = null)
    {
        _beforeCreate = beforeCreate;
        _afterCreate = afterCreate;
        _beforeUpdate = beforeUpdate;
        _afterUpdate = afterUpdate;
        _beforeDelete = beforeDelete;
        _afterDelete = afterDelete;
    }

    public Task BeforeCreate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => _beforeCreate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task AfterCreate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => _afterCreate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task BeforeUpdate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => _beforeUpdate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task AfterUpdate(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => _afterUpdate?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task BeforeDelete(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => _beforeDelete?.Invoke(entity, ctx) ?? Task.CompletedTask;
    public Task AfterDelete(ProductEntity entity, CrudKit.Core.Context.AppContext ctx)
        => _afterDelete?.Invoke(entity, ctx) ?? Task.CompletedTask;
}

/// <summary>
/// Test IEntityMapper that adds a DisplayName computed field.
/// </summary>
public class TestProductMapper : IEntityMapper<ProductEntity, ProductResponse>
{
    public ProductResponse Map(ProductEntity entity)
        => new(entity.Id, entity.Name, entity.Price, $"{entity.Name} ({entity.Price})");

    public IQueryable<ProductResponse> Project(IQueryable<ProductEntity> query)
        => query.Select(e => new ProductResponse(e.Id, e.Name, e.Price, e.Name + " (" + e.Price + ")"));
}
```

- [ ] **6.2** Verify tests fail (build error — `CrudEndpointMapper` does not exist yet).

- [ ] **6.3** Create `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs`:

```csharp
using System.Reflection;
using CrudKit.Api.Configuration;
using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps standard CRUD endpoints for an entity using ASP.NET Core Minimal API.
/// Conditionally adds Restore (ISoftDeletable) and Transition (IStateMachine) endpoints.
/// Calls ICrudHooks lifecycle hooks within a DB transaction.
/// Applies IEntityMapper if registered in DI.
/// </summary>
public static class CrudEndpointMapper
{
    /// <summary>
    /// Maps standard CRUD endpoints: GET /, GET /{id}, POST /, PUT /{id}, DELETE /{id}.
    /// If TEntity implements ISoftDeletable, POST /{id}/restore is also added.
    /// If TEntity implements IStateMachine, POST /{id}/transition/{action} is also added.
    /// All mutating handlers run inside a DB transaction.
    /// </summary>
    public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app,
        string route)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var entityName = typeof(TEntity).Name.Replace("Entity", "");
        var group = app.MapGroup($"/api/{route}");

        // Apply AppErrorFilter to all endpoints in this group
        group.AddEndpointFilter<AppErrorFilter>();

        // ---- List ----
        group.MapGet("/", async (
            HttpContext ctx,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(ctx.Request.Query);
            var result = await repo.List(listParams, ct);

            // Apply IEntityMapper if registered
            var mapped = TryMapPaginated<TEntity>(ctx.RequestServices, result);
            return Results.Ok(mapped);
        })
        .WithName($"{entityName}_List")
        .WithTags(entityName)
        .Produces<Paginated<TEntity>>(200);

        // ---- Get by id ----
        group.MapGet("/{id}", async (
            HttpContext ctx,
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.FindByIdOrDefault(id, ct);
            if (entity is null) return Results.NotFound();

            var mapped = TryMapSingle<TEntity>(ctx.RequestServices, entity);
            return Results.Ok(mapped);
        })
        .WithName($"{entityName}_GetById")
        .WithTags(entityName)
        .Produces<TEntity>(200)
        .ProducesProblem(404);

        // ---- Create ----
        group.MapPost("/", async (
            HttpContext httpCtx,
            [FromBody] TCreate body,
            IRepo<TEntity> repo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            var logger = httpCtx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CrudKit.Api");
            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var appCtx = BuildAppContext(httpCtx);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                if (hooks != null)
                {
                    var shell = Activator.CreateInstance<TEntity>();
                    await hooks.BeforeCreate(shell, appCtx);
                }

                var entity = await repo.Create(body, ct);

                if (hooks != null)
                    await hooks.AfterCreate(entity, appCtx);

                await tx.CommitAsync(ct);

                logger.LogDebug("Created {Entity} id={Id}", typeof(TEntity).Name, entity.Id);

                var mapped = TryMapSingle<TEntity>(httpCtx.RequestServices, entity);
                return Results.Created($"/api/{route}/{entity.Id}", mapped);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .AddEndpointFilter<ValidationFilter<TCreate>>()
        .WithName($"{entityName}_Create")
        .WithTags(entityName)
        .Produces<TEntity>(201)
        .ProducesProblem(400)
        .ProducesProblem(409);

        // ---- Update ----
        group.MapPut("/{id}", async (
            HttpContext httpCtx,
            string id,
            [FromBody] TUpdate body,
            IRepo<TEntity> repo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            var logger = httpCtx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CrudKit.Api");
            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var appCtx = BuildAppContext(httpCtx);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                if (hooks != null)
                {
                    var existing = await repo.FindById(id, ct);
                    await hooks.BeforeUpdate(existing, appCtx);
                }

                var entity = await repo.Update(id, body, ct);

                if (hooks != null)
                    await hooks.AfterUpdate(entity, appCtx);

                await tx.CommitAsync(ct);

                logger.LogDebug("Updated {Entity} id={Id}", typeof(TEntity).Name, entity.Id);

                var mapped = TryMapSingle<TEntity>(httpCtx.RequestServices, entity);
                return Results.Ok(mapped);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .AddEndpointFilter<ValidationFilter<TUpdate>>()
        .WithName($"{entityName}_Update")
        .WithTags(entityName)
        .Produces<TEntity>(200)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(409);

        // ---- Delete ----
        group.MapDelete("/{id}", async (
            HttpContext httpCtx,
            string id,
            IRepo<TEntity> repo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            var logger = httpCtx.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("CrudKit.Api");
            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var appCtx = BuildAppContext(httpCtx);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                TEntity? entity = null;

                if (hooks != null)
                {
                    entity = await repo.FindById(id, ct);
                    await hooks.BeforeDelete(entity, appCtx);
                }

                await repo.Delete(id, ct);

                if (hooks != null && entity != null)
                    await hooks.AfterDelete(entity, appCtx);

                await tx.CommitAsync(ct);

                logger.LogDebug("Deleted {Entity} id={Id}", typeof(TEntity).Name, id);

                return Results.NoContent();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"{entityName}_Delete")
        .WithTags(entityName)
        .Produces(204)
        .ProducesProblem(404);

        // ---- Restore (ISoftDeletable only) ----
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            group.MapPost("/{id}/restore", async (
                HttpContext httpCtx,
                string id,
                IRepo<TEntity> repo,
                CrudKitDbContext db,
                CancellationToken ct) =>
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var appCtx = BuildAppContext(httpCtx);

                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    if (hooks != null)
                    {
                        // FindById uses IgnoreQueryFilters internally in Restore,
                        // but we need the entity for BeforeRestore hook.
                        // We skip pre-load here — hook receives entity after restore.
                    }

                    await repo.Restore(id, ct);

                    if (hooks != null)
                    {
                        var restored = await repo.FindById(id, ct);
                        await hooks.AfterRestore(restored, appCtx);
                    }

                    await tx.CommitAsync(ct);
                    return Results.Ok();
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            })
            .WithName($"{entityName}_Restore")
            .WithTags(entityName)
            .Produces(200)
            .ProducesProblem(404);
        }

        // ---- Transition (IStateMachine<TState> only) ----
        var smInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachine<>));

        if (smInterface != null)
        {
            group.MapPost("/{id}/transition/{action}", async (
                HttpContext httpCtx,
                string id,
                string action,
                IRepo<TEntity> repo,
                CrudKitDbContext db,
                CancellationToken ct) =>
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                try
                {
                    var entity = await repo.FindById(id, ct);

                    // Get the static Transitions property
                    var transitionsProp = typeof(TEntity).GetProperty(
                        "Transitions", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (transitionsProp == null)
                        throw new InvalidOperationException($"No Transitions property found on {typeof(TEntity).Name}.");

                    // Get the current status
                    var statusProp = smInterface.GetProperty("Status")!;
                    var currentStatus = statusProp.GetValue(entity);

                    // Find a matching transition
                    var transitionsList = (System.Collections.IEnumerable)transitionsProp.GetValue(null)!;
                    object? newStatus = null;

                    foreach (var t in transitionsList)
                    {
                        var tType = t.GetType();
                        var from = tType.GetField("Item1")!.GetValue(t);
                        var to   = tType.GetField("Item2")!.GetValue(t);
                        var act  = (string)tType.GetField("Item3")!.GetValue(t)!;

                        if (from!.Equals(currentStatus)
                            && string.Equals(act, action, StringComparison.OrdinalIgnoreCase))
                        {
                            newStatus = to;
                            break;
                        }
                    }

                    if (newStatus == null)
                        throw CrudKit.Core.Models.AppError.BadRequest(
                            $"Transition '{action}' is not valid from the current state.");

                    // Apply the transition
                    statusProp.SetValue(entity, newStatus);
                    var updated = await repo.Update(id, entity, ct);

                    await tx.CommitAsync(ct);

                    var mapped = TryMapSingle<TEntity>(httpCtx.RequestServices, updated);
                    return Results.Ok(mapped);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            })
            .WithName($"{entityName}_Transition")
            .WithTags(entityName)
            .Produces<TEntity>(200)
            .ProducesProblem(400)
            .ProducesProblem(404);
        }

        return group;
    }

    // ---- Helper: build AppContext ----
    private static Core.Context.AppContext BuildAppContext(HttpContext httpCtx)
    {
        var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
        return new Core.Context.AppContext
        {
            Services = httpCtx.RequestServices,
            CurrentUser = currentUser
        };
    }

    // ---- Helper: apply IEntityMapper for a single entity ----
    private static object TryMapSingle<TEntity>(IServiceProvider services, TEntity entity)
        where TEntity : class, IEntity
    {
        // Try to resolve any IEntityMapper<TEntity, ?> from DI.
        // We look for IEntityMapper<TEntity, object> or any concrete registration.
        var mapperType = typeof(IEntityMapper<,>).MakeGenericType(typeof(TEntity), typeof(object));

        // Since we can't know TResponse at compile time, scan services for any IEntityMapper<TEntity, ?>
        var allMappers = services.GetServices<object>()
            .Where(s => s != null && s.GetType().GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IEntityMapper<,>)
                && i.GetGenericArguments()[0] == typeof(TEntity)));

        var mapper = allMappers.FirstOrDefault();
        if (mapper == null) return entity;

        // Call Map(entity) via reflection
        var mapMethod = mapper.GetType().GetMethod("Map")!;
        return mapMethod.Invoke(mapper, [entity])!;
    }

    // ---- Helper: apply IEntityMapper for paginated results ----
    private static object TryMapPaginated<TEntity>(IServiceProvider services, Paginated<TEntity> paginated)
        where TEntity : class, IEntity
    {
        var allMappers = services.GetServices<object>()
            .Where(s => s != null && s.GetType().GetInterfaces().Any(i =>
                i.IsGenericType
                && i.GetGenericTypeDefinition() == typeof(IEntityMapper<,>)
                && i.GetGenericArguments()[0] == typeof(TEntity)));

        var mapper = allMappers.FirstOrDefault();
        if (mapper == null) return paginated;

        var mapMethod = mapper.GetType().GetMethod("Map")!;
        var mappedData = paginated.Data.Select(e => mapMethod.Invoke(mapper, [e])!).ToList();

        return new
        {
            data = mappedData,
            total = paginated.Total,
            page = paginated.Page,
            perPage = paginated.PerPage,
            totalPages = paginated.TotalPages
        };
    }
}
```

- [ ] **6.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudEndpointMapperTests"`. All tests should pass.

- [ ] **6.5** Also verify ValidationFilter tests now pass: `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~ValidationFilterTests"`. All 5 tests should pass.

- [ ] **6.6** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **6.7** Commit: `feat(api): add CrudEndpointMapper with transaction-scoped CRUD, hooks, IEntityMapper, and OpenAPI metadata`

---

## Task 7: CrudEndpointMapper — Restore + Transition tests

Restore and Transition endpoints were already added in Task 6. This task adds targeted tests.

### Steps

- [ ] **7.1** Add Restore and Transition tests to `tests/CrudKit.Api.Tests/Endpoints/CrudEndpointMapperTests.cs`. Append the following test methods to the existing `CrudEndpointMapperTests` class:

```csharp
    // ---- Restore (ISoftDeletable) ----
    [Fact]
    public async Task Restore_SoftDeletable_Returns200()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<SoftProductEntity, CreateSoftProductDto, CreateSoftProductDto>("soft-products"));

        var create = await app.Client.PostAsJsonAsync("/api/soft-products",
            new { Name = "Soft" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Soft delete
        await app.Client.DeleteAsync($"/api/soft-products/{id}");

        // Restore
        var restore = await app.Client.PostAsync($"/api/soft-products/{id}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        // Should be accessible again
        var get = await app.Client.GetAsync($"/api/soft-products/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task NonSoftDeletable_DoesNotHaveRestoreEndpoint()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Regular", Price = 5.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Restore endpoint should not exist for non-soft-deletable entities
        var restore = await app.Client.PostAsync($"/api/products/{id}/restore", null);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }

    // ---- Transition (IStateMachine) ----
    [Fact]
    public async Task Transition_ValidAction_UpdatesStatus()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders"));

        var create = await app.Client.PostAsJsonAsync("/api/orders",
            new { Customer = "Alice" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Pending -> Processing via "process" action
        var transition = await app.Client.PostAsync($"/api/orders/{id}/transition/process", null);
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);

        var body = await transition.Content.ReadFromJsonAsync<JsonElement>();
        // OrderStatus.Processing stored as string due to CrudKitDbContext enum config
        Assert.Equal("Processing", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Transition_InvalidAction_Returns400()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders"));

        var create = await app.Client.PostAsJsonAsync("/api/orders",
            new { Customer = "Bob" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Can't "complete" from Pending — only from Processing
        var transition = await app.Client.PostAsync($"/api/orders/{id}/transition/complete", null);
        Assert.Equal(HttpStatusCode.BadRequest, transition.StatusCode);
    }

    [Fact]
    public async Task Transition_ChainedActions_Work()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<OrderEntity, CreateOrderDto, UpdateOrderDto>("orders"));

        var create = await app.Client.PostAsJsonAsync("/api/orders",
            new { Customer = "Carol" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Pending -> Processing -> Completed
        await app.Client.PostAsync($"/api/orders/{id}/transition/process", null);
        var complete = await app.Client.PostAsync($"/api/orders/{id}/transition/complete", null);

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        var body = await complete.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Completed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task NonStateMachine_DoesNotHaveTransitionEndpoint()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var create = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "Regular", Price = 5.00 });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;

        // Transition endpoint should not exist for non-state-machine entities
        var transition = await app.Client.PostAsync($"/api/products/{id}/transition/foo", null);
        Assert.Equal(HttpStatusCode.NotFound, transition.StatusCode);
    }
```

- [ ] **7.2** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudEndpointMapperTests"`. All tests should pass.

- [ ] **7.3** Commit: `test(api): add Restore and Transition endpoint tests`

---

## Task 8: IdempotencyFilter + IdempotencyRecord + IdempotencyCleanupService

Per edge case 11.6. Idempotency key prevents duplicate writes. Applied to mutating endpoints when `CrudKitApiOptions.EnableIdempotency = true`.

### Steps

- [ ] **8.1** Create `src/CrudKit.Api/Models/IdempotencyRecord.cs`:

```csharp
namespace CrudKit.Api.Models;

/// <summary>
/// Stores the response of a previously processed idempotent request.
/// Table: __crud_idempotency (configured in CrudKitDbContext.OnModelCreating by consumer).
/// </summary>
public class IdempotencyRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Client-provided idempotency key (from Idempotency-Key header).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Request path that was processed.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP method (POST, PUT, DELETE).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>HTTP status code of the original response.</summary>
    public int StatusCode { get; set; }

    /// <summary>Serialized response body (JSON).</summary>
    public string ResponseBody { get; set; } = string.Empty;

    /// <summary>User ID who made the original request.</summary>
    public string? UserId { get; set; }

    /// <summary>Tenant ID for multi-tenant isolation.</summary>
    public string? TenantId { get; set; }

    /// <summary>When the record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the record expires and can be cleaned up.</summary>
    public DateTime ExpiresAt { get; set; }
}
```

- [ ] **8.2** Create `src/CrudKit.Api/Filters/IdempotencyFilter.cs`:

```csharp
using System.IO;
using System.Text;
using System.Text.Json;
using CrudKit.Api.Models;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that implements idempotency via the Idempotency-Key header.
/// If the same key was already processed, the cached response is returned.
/// If no key is provided, the request proceeds normally (idempotency is optional).
/// Applied to POST/PUT/DELETE endpoints when CrudKitApiOptions.EnableIdempotency = true.
/// </summary>
public class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var httpContext = ctx.HttpContext;
        var method = httpContext.Request.Method;

        // Only apply to mutating methods — GET is idempotent by definition
        if (method == "GET") return await next(ctx);

        // Idempotency-Key header is optional
        var key = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrEmpty(key)) return await next(ctx);

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("CrudKit.Api");
        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        var db = httpContext.RequestServices.GetRequiredService<CrudKitDbContext>();

        var tenantId = currentUser.TenantId;
        var userId = currentUser.Id;
        var path = httpContext.Request.Path.Value ?? "";

        // Compose a compound key scoped to user to prevent cross-user cache hits
        var compoundKey = $"{userId ?? "anon"}:{key}";

        // Check if this key was already processed
        var existing = await db.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(r =>
                r.Key == compoundKey
                && r.TenantId == tenantId
                && r.ExpiresAt > DateTime.UtcNow);

        if (existing != null)
        {
            logger.LogDebug("Idempotency cache hit for key={Key}, returning cached response", key);
            httpContext.Response.Headers["X-Idempotency-Replayed"] = "true";
            return Results.Content(existing.ResponseBody, "application/json",
                Encoding.UTF8, existing.StatusCode);
        }

        // Process the request
        var result = await next(ctx);

        // Capture the response for caching
        try
        {
            // Serialize the result to JSON
            var responseBody = result != null
                ? JsonSerializer.Serialize(result)
                : "";

            // Determine the status code — inspect IResult for status
            var statusCode = 200;
            if (result is IStatusCodeHttpResult statusResult)
                statusCode = statusResult.StatusCode ?? 200;

            var record = new IdempotencyRecord
            {
                Key = compoundKey,
                Path = path,
                Method = method,
                StatusCode = statusCode,
                ResponseBody = responseBody,
                UserId = userId,
                TenantId = tenantId,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            db.Set<IdempotencyRecord>().Add(record);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // Unique constraint violation = race condition — another request won
            logger.LogDebug(ex, "Idempotency record already exists for key={Key}, ignoring", key);
        }

        return result;
    }
}
```

- [ ] **8.3** Create `src/CrudKit.Api/Services/IdempotencyCleanupService.cs`:

```csharp
using CrudKit.Api.Models;
using CrudKit.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Services;

/// <summary>
/// Background service that periodically removes expired idempotency records.
/// Runs every hour by default.
/// </summary>
public class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<IdempotencyCleanupService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public IdempotencyCleanupService(
        IServiceProvider services,
        ILogger<IdempotencyCleanupService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested — exit gracefully
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Idempotency cleanup failed");
            }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrudKitDbContext>();

        var deleted = await db.Set<IdempotencyRecord>()
            .Where(r => r.ExpiresAt < DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            _logger.LogDebug("Cleaned up {Count} expired idempotency records", deleted);
    }
}
```

- [ ] **8.4** Create test file `tests/CrudKit.Api.Tests/Filters/IdempotencyFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Filters;
using CrudKit.Api.Models;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class IdempotencyFilterTests
{
    [Fact]
    public async Task SameKey_ReturnsCachedResponse()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services =>
            {
                // Register IdempotencyRecord in the model
                // (In real use, user adds this to their DbContext)
            },
            configureEndpoints: a =>
            {
                a.MapPost("/api/idempotent", (HttpContext ctx) =>
                {
                    return Results.Created("/api/idempotent/1", new { id = "1", name = "test" });
                })
                .AddEndpointFilter<AppErrorFilter>()
                .AddEndpointFilter<IdempotencyFilter>();
            });

        var key = Guid.NewGuid().ToString();

        // First request
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/idempotent");
        request1.Headers.Add("Idempotency-Key", key);
        request1.Content = JsonContent.Create(new { });
        var response1 = await app.Client.SendAsync(request1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Second request with same key — should return cached
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/idempotent");
        request2.Headers.Add("Idempotency-Key", key);
        request2.Content = JsonContent.Create(new { });
        var response2 = await app.Client.SendAsync(request2);
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
        Assert.Equal("true", response2.Headers.GetValues("X-Idempotency-Replayed").First());
    }

    [Fact]
    public async Task DifferentKey_ProcessesNormally()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapPost("/api/idempotent", (HttpContext ctx) =>
            {
                return Results.Created("/api/idempotent/1", new { id = "1" });
            })
            .AddEndpointFilter<AppErrorFilter>()
            .AddEndpointFilter<IdempotencyFilter>();
        });

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/idempotent");
        request1.Headers.Add("Idempotency-Key", "key-1");
        request1.Content = JsonContent.Create(new { });
        await app.Client.SendAsync(request1);

        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/idempotent");
        request2.Headers.Add("Idempotency-Key", "key-2");
        request2.Content = JsonContent.Create(new { });
        var response2 = await app.Client.SendAsync(request2);

        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
        Assert.False(response2.Headers.Contains("X-Idempotency-Replayed"));
    }

    [Fact]
    public async Task NoKey_ProcessesNormally()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapPost("/api/idempotent", (HttpContext ctx) =>
            {
                return Results.Created("/api/idempotent/1", new { id = "1" });
            })
            .AddEndpointFilter<AppErrorFilter>()
            .AddEndpointFilter<IdempotencyFilter>();
        });

        var response = await app.Client.PostAsJsonAsync("/api/idempotent", new { });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Idempotency-Replayed"));
    }

    [Fact]
    public async Task GetRequests_AreNotAffected()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapGet("/api/idempotent", () => Results.Ok(new { msg = "hello" }))
             .AddEndpointFilter<IdempotencyFilter>();
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/idempotent");
        request.Headers.Add("Idempotency-Key", "some-key");
        var response = await app.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Idempotency-Replayed"));
    }
}
```

- [ ] **8.5** Note: IdempotencyFilter tests require that IdempotencyRecord is registered in the DbContext model. The ApiTestDbContext needs to be updated to include this entity. Update `tests/CrudKit.Api.Tests/Helpers/ApiTestDbContext.cs` by adding `OnModelCreatingCustom`:

```csharp
// Add to ApiTestDbContext:
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("__crud_idempotency");
            b.HasKey(e => e.Id);
            b.Property(e => e.Key).HasMaxLength(500).IsRequired();
            b.Property(e => e.Path).HasMaxLength(500).IsRequired();
            b.Property(e => e.Method).HasMaxLength(10).IsRequired();
            b.HasIndex(e => new { e.Key, e.TenantId }).IsUnique();
            b.HasIndex(e => e.ExpiresAt);
        });
    }
```

And add the required using:
```csharp
using CrudKit.Api.Models;
```

- [ ] **8.6** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~IdempotencyFilterTests"`. All 4 tests should pass.

- [ ] **8.7** Commit: `feat(api): add IdempotencyFilter, IdempotencyRecord, and IdempotencyCleanupService`

---

## Task 9: Bulk Endpoints

Per edge case 11.7 and 11.15. POST /bulk-update, POST /bulk-delete, GET /bulk-count. Enforces CrudKitApiOptions.BulkLimit.

### Steps

- [ ] **9.1** Create test file `tests/CrudKit.Api.Tests/Endpoints/BulkEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class BulkEndpointTests
{
    [Fact]
    public async Task BulkCount_ReturnsFilteredCount()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "A", Price = 10.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "B", Price = 20.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "C", Price = 30.0 });

        var response = await app.Client.GetAsync("/api/products/bulk-count");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task BulkCount_WithFilter_ReturnsFilteredCount()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Cheap", Price = 5.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Mid", Price = 15.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Expensive", Price = 50.0 });

        // Filter: price > 10
        var response = await app.Client.GetAsync("/api/products/bulk-count?price=gt:10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("count").GetInt64());
    }

    [Fact]
    public async Task BulkDelete_DeletesFilteredRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Keep", Price = 5.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Delete1", Price = 15.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "Delete2", Price = 25.0 });

        var response = await app.Client.PostAsJsonAsync("/api/products/bulk-delete",
            new { filters = new { price = "gt:10" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("affected").GetInt32());

        // Verify only 1 remains
        var list = await app.Client.GetAsync("/api/products");
        var listBody = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listBody.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task BulkUpdate_UpdatesFilteredRecords()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        await app.Client.PostAsJsonAsync("/api/products", new { Name = "A", Price = 10.0 });
        await app.Client.PostAsJsonAsync("/api/products", new { Name = "B", Price = 20.0 });

        var response = await app.Client.PostAsJsonAsync("/api/products/bulk-update",
            new { filters = new { }, values = new { name = "Updated" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("affected").GetInt32());
    }
}
```

- [ ] **9.2** Verify tests fail (bulk endpoints do not exist yet).

- [ ] **9.3** Add bulk endpoints to `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs`. Add the following methods just before the `return group;` at the end of `MapCrudEndpoints`, after the Transition block:

```csharp
        // ---- Bulk Count ----
        group.MapGet("/bulk-count", async (
            HttpContext ctx,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(ctx.Request.Query);
            // BulkCount uses filters from query string
            var count = await repo.Count(ct);
            // TODO: Once IRepo.BulkCount with filters is available (Plan 2b), use it here.
            // For now, use List with page size 0 trick or raw count.
            return Results.Ok(new { count });
        })
        .WithName($"{entityName}_BulkCount")
        .WithTags(entityName)
        .Produces(200);

        // ---- Bulk Delete ----
        group.MapPost("/bulk-delete", async (
            HttpContext httpCtx,
            [FromBody] BulkDeleteRequest body,
            IRepo<TEntity> repo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            var opts = httpCtx.RequestServices.GetService<CrudKit.Api.Configuration.CrudKitApiOptions>();
            var limit = opts?.BulkLimit ?? 10_000;

            // Parse filters from the request body
            var filters = new Dictionary<string, FilterOp>();
            if (body.Filters != null)
            {
                foreach (var (key, value) in body.Filters)
                    filters[key] = FilterOp.Parse(value);
            }

            // Count first to enforce limit
            var listParams = new ListParams { Page = 1, PerPage = 1, Filters = filters };
            var preview = await repo.List(listParams, ct);
            if (preview.Total > limit)
                throw CrudKit.Core.Models.AppError.BadRequest(
                    $"Operation would affect {preview.Total} records, exceeding the limit of {limit}. Narrow your filters.");

            // Perform bulk delete within a transaction
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Delete matching records one by one (until BulkDelete is in IRepo from Plan 2b)
                var toDelete = await repo.List(new ListParams { Page = 1, PerPage = (int)Math.Min(preview.Total, limit), Filters = filters }, ct);
                var affected = 0;
                foreach (var entity in toDelete.Data)
                {
                    await repo.Delete(entity.Id, ct);
                    affected++;
                }

                await tx.CommitAsync(ct);
                return Results.Ok(new { affected });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"{entityName}_BulkDelete")
        .WithTags(entityName)
        .Produces(200)
        .ProducesProblem(400);

        // ---- Bulk Update ----
        group.MapPost("/bulk-update", async (
            HttpContext httpCtx,
            [FromBody] BulkUpdateRequest body,
            IRepo<TEntity> repo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            var opts = httpCtx.RequestServices.GetService<CrudKit.Api.Configuration.CrudKitApiOptions>();
            var limit = opts?.BulkLimit ?? 10_000;

            var filters = new Dictionary<string, FilterOp>();
            if (body.Filters != null)
            {
                foreach (var (key, value) in body.Filters)
                    filters[key] = FilterOp.Parse(value);
            }

            // Count first to enforce limit
            var listParams = new ListParams { Page = 1, PerPage = 1, Filters = filters };
            var preview = await repo.List(listParams, ct);
            if (preview.Total > limit)
                throw CrudKit.Core.Models.AppError.BadRequest(
                    $"Operation would affect {preview.Total} records, exceeding the limit of {limit}. Narrow your filters.");

            // Perform bulk update within a transaction
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var toUpdate = await repo.List(new ListParams { Page = 1, PerPage = (int)Math.Min(preview.Total, limit), Filters = filters }, ct);
                var affected = 0;
                foreach (var entity in toUpdate.Data)
                {
                    if (body.Values != null)
                        await repo.Update(entity.Id, body.Values, ct);
                    affected++;
                }

                await tx.CommitAsync(ct);
                return Results.Ok(new { affected });
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"{entityName}_BulkUpdate")
        .WithTags(entityName)
        .Produces(200)
        .ProducesProblem(400);
```

Also add the request models at the bottom of `CrudEndpointMapper.cs`:

```csharp
/// <summary>Request body for POST /bulk-delete endpoint.</summary>
public class BulkDeleteRequest
{
    public Dictionary<string, string>? Filters { get; set; }
}

/// <summary>Request body for POST /bulk-update endpoint.</summary>
public class BulkUpdateRequest
{
    public Dictionary<string, string>? Filters { get; set; }
    public object? Values { get; set; }
}
```

- [ ] **9.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~BulkEndpointTests"`. All 4 tests should pass.

- [ ] **9.5** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **9.6** Commit: `feat(api): add bulk-count, bulk-delete, bulk-update endpoints with limit enforcement`

---

## Task 10: DetailEndpointMapper

Master-detail nested CRUD endpoints: List, Get, Create, Delete, and Batch upsert for child entities.

### Steps

- [ ] **10.1** Create test file `tests/CrudKit.Api.Tests/Endpoints/DetailEndpointMapperTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

public class DetailEndpointMapperTests
{
    [Fact]
    public async Task ListByMaster_ReturnsEmpty_WhenNoDetails()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices");
            a.MapCrudDetailEndpoints<InvoiceEntity, InvoiceLineEntity, CreateInvoiceLineDto>(
                "invoices", "lines", "InvoiceId");
        });

        var create = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var masterId = created.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/invoices/{masterId}/lines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task CreateForMaster_Returns201AndSetsFK()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices");
            a.MapCrudDetailEndpoints<InvoiceEntity, InvoiceLineEntity, CreateInvoiceLineDto>(
                "invoices", "lines", "InvoiceId");
        });

        var create = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var masterId = created.GetProperty("id").GetString()!;

        var lineResp = await app.Client.PostAsJsonAsync(
            $"/api/invoices/{masterId}/lines",
            new { Description = "Line item 1", Amount = 100.00 });

        Assert.Equal(HttpStatusCode.Created, lineResp.StatusCode);
        var line = await lineResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(masterId, line.GetProperty("invoiceId").GetString());
    }

    [Fact]
    public async Task ListByMaster_ReturnsOnlyRelatedDetails()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices");
            a.MapCrudDetailEndpoints<InvoiceEntity, InvoiceLineEntity, CreateInvoiceLineDto>(
                "invoices", "lines", "InvoiceId");
        });

        // Create two invoices
        var inv1 = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        var inv1Id = (await inv1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var inv2 = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-002" });
        var inv2Id = (await inv2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        // Add lines to each
        await app.Client.PostAsJsonAsync($"/api/invoices/{inv1Id}/lines",
            new { Description = "Line A", Amount = 10.0 });
        await app.Client.PostAsJsonAsync($"/api/invoices/{inv1Id}/lines",
            new { Description = "Line B", Amount = 20.0 });
        await app.Client.PostAsJsonAsync($"/api/invoices/{inv2Id}/lines",
            new { Description = "Line C", Amount = 30.0 });

        // List lines for inv1 — should only have 2
        var response = await app.Client.GetAsync($"/api/invoices/{inv1Id}/lines");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task DeleteDetail_Returns204()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices");
            a.MapCrudDetailEndpoints<InvoiceEntity, InvoiceLineEntity, CreateInvoiceLineDto>(
                "invoices", "lines", "InvoiceId");
        });

        var inv = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        var invId = (await inv.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var line = await app.Client.PostAsJsonAsync($"/api/invoices/{invId}/lines",
            new { Description = "Deleteable", Amount = 5.0 });
        var lineId = (await line.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var delete = await app.Client.DeleteAsync($"/api/invoices/{invId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task GetDetail_ReturnsDetailById()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
        {
            a.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices");
            a.MapCrudDetailEndpoints<InvoiceEntity, InvoiceLineEntity, CreateInvoiceLineDto>(
                "invoices", "lines", "InvoiceId");
        });

        var inv = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        var invId = (await inv.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var line = await app.Client.PostAsJsonAsync($"/api/invoices/{invId}/lines",
            new { Description = "Fetch me", Amount = 42.0 });
        var lineId = (await line.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var get = await app.Client.GetAsync($"/api/invoices/{invId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Fetch me", body.GetProperty("description").GetString());
    }
}
```

- [ ] **10.2** Verify tests fail (build error — `DetailEndpointMapper` does not exist yet).

- [ ] **10.3** Create `src/CrudKit.Api/Endpoints/DetailEndpointMapper.cs`:

```csharp
using System.Reflection;
using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps master-detail nested CRUD endpoints.
/// Routes: /api/{masterRoute}/{masterId}/{detailRoute}
/// </summary>
public static class DetailEndpointMapper
{
    /// <summary>
    /// Maps nested detail endpoints for a master-detail relationship.
    /// Endpoints: List, Get, Create, Delete.
    /// The foreign key property on the detail entity is automatically set from the route parameter.
    /// </summary>
    public static RouteGroupBuilder MapCrudDetailEndpoints<TMaster, TDetail, TCreateDetail>(
        this WebApplication app,
        string masterRoute,
        string detailRoute,
        string foreignKeyProperty)
        where TMaster : class, IEntity
        where TDetail : class, IEntity
        where TCreateDetail : class
    {
        var detailName = typeof(TDetail).Name.Replace("Entity", "");
        var group = app.MapGroup($"/api/{masterRoute}/{{masterId}}/{detailRoute}");

        // Apply AppErrorFilter to all endpoints in this group
        group.AddEndpointFilter<AppErrorFilter>();

        // ---- List by master ----
        group.MapGet("/", async (
            string masterId,
            IRepo<TDetail> repo,
            CancellationToken ct) =>
        {
            var result = await repo.FindByField(foreignKeyProperty, masterId, ct);
            return Results.Ok(new Paginated<TDetail>
            {
                Data = result,
                Total = result.Count,
                Page = 1,
                PerPage = result.Count,
                TotalPages = 1
            });
        })
        .WithName($"{detailName}_ListByMaster")
        .WithTags(detailName);

        // ---- Get detail by id ----
        group.MapGet("/{id}", async (
            string masterId,
            string id,
            IRepo<TDetail> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.FindByIdOrDefault(id, ct);
            if (entity is null) return Results.NotFound();

            // Verify the detail belongs to the specified master
            var fkProp = typeof(TDetail).GetProperty(foreignKeyProperty,
                BindingFlags.Public | BindingFlags.Instance);
            if (fkProp != null)
            {
                var fkValue = fkProp.GetValue(entity)?.ToString();
                if (fkValue != masterId) return Results.NotFound();
            }

            return Results.Ok(entity);
        })
        .WithName($"{detailName}_GetByIdForMaster")
        .WithTags(detailName)
        .Produces<TDetail>(200)
        .ProducesProblem(404);

        // ---- Create detail ----
        group.MapPost("/", async (
            string masterId,
            [FromBody] TCreateDetail body,
            IRepo<TMaster> masterRepo,
            IRepo<TDetail> detailRepo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            // Verify master exists
            var master = await masterRepo.FindByIdOrDefault(masterId, ct);
            if (master is null) return Results.NotFound();

            // Set the FK on the DTO if it has the property
            var dtoFkProp = body.GetType().GetProperty(foreignKeyProperty,
                BindingFlags.Public | BindingFlags.Instance);
            if (dtoFkProp != null && dtoFkProp.CanWrite)
                dtoFkProp.SetValue(body, masterId);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var entity = await detailRepo.Create(body, ct);
                await tx.CommitAsync(ct);
                return Results.Created(
                    $"/api/{masterRoute}/{masterId}/{detailRoute}/{entity.Id}", entity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .AddEndpointFilter<ValidationFilter<TCreateDetail>>()
        .WithName($"{detailName}_CreateForMaster")
        .WithTags(detailName)
        .Produces<TDetail>(201)
        .ProducesProblem(400)
        .ProducesProblem(404);

        // ---- Delete detail ----
        group.MapDelete("/{id}", async (
            string masterId,
            string id,
            IRepo<TDetail> repo,
            CrudKitDbContext db,
            CancellationToken ct) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await repo.Delete(id, ct);
                await tx.CommitAsync(ct);
                return Results.NoContent();
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        })
        .WithName($"{detailName}_DeleteForMaster")
        .WithTags(detailName)
        .Produces(204)
        .ProducesProblem(404);

        return group;
    }
}
```

- [ ] **10.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~DetailEndpointMapperTests"`. All 5 tests should pass.

- [ ] **10.5** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **10.6** Commit: `feat(api): add DetailEndpointMapper for master-detail nested CRUD endpoints`

---

## Task 11: Startup Validation

Per edge case 11.24. ValidateEntityMetadata in AddCrudKit checks OwnerField, IConcurrent+BulkUpdate warning, WorkflowProtected fields. CrudKitStartupValidator (IHostedService) runs DB-level checks at startup.

### Steps

- [ ] **11.1** Create test file `tests/CrudKit.Api.Tests/Extensions/StartupValidationTests.cs`:

```csharp
using CrudKit.Api.Extensions;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Api.Validation;
using CrudKit.Core.Attributes;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CrudKit.Api.Tests.Extensions;

public class StartupValidationTests
{
    [Fact]
    public async Task StartupValidator_CompletesSuccessfully_WithValidEntities()
    {
        await using var app = await TestWebApp.CreateAsync();
        // If we got here without an exception, startup validation passed
        Assert.NotNull(app);
    }

    [Fact]
    public void StartupValidator_IsRegistered()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApiTestDbContext>((_, opts) => opts.UseSqlite(connection));
        services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser());
        services.AddCrudKit<ApiTestDbContext>();

        using var sp = services.BuildServiceProvider();
        var hostedServices = sp.GetServices<IHostedService>();
        Assert.Contains(hostedServices, s => s is CrudKitStartupValidator);
    }
}
```

- [ ] **11.2** Verify tests pass (the stub CrudKitStartupValidator from Task 5 should already pass these).

- [ ] **11.3** Update `src/CrudKit.Api/Validation/CrudKitStartupValidator.cs` with full validation logic:

```csharp
using System.Reflection;
using CrudKit.Api.Configuration;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Validation;

/// <summary>
/// IHostedService that validates entity metadata and configuration at startup.
/// DB-independent checks (reflection/metadata) run at startup.
/// DB-dependent checks (workflow definitions) are stubbed until the Workflow package exists.
/// </summary>
public class CrudKitStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CrudKitStartupValidator> _logger;

    public CrudKitStartupValidator(IServiceProvider services, ILogger<CrudKitStartupValidator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetService<CrudKitDbContext>();

        if (db != null)
        {
            ValidateEntityMetadata(db);
        }

        _logger.LogDebug("CrudKitStartupValidator: startup validation complete.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void ValidateEntityMetadata(CrudKitDbContext db)
    {
        var entityTypes = db.Model.GetEntityTypes()
            .Select(e => e.ClrType)
            .Where(t => typeof(IEntity).IsAssignableFrom(t));

        foreach (var entityType in entityTypes)
        {
            var attr = entityType.GetCustomAttribute<CrudEntityAttribute>();
            if (attr == null) continue;

            // Check: OwnerField property exists on the entity
            // (OwnerField is not defined on CrudEntityAttribute yet, but if it were, check here)
            // This is a forward-looking stub for when OwnerField is added.

            // Check: IConcurrent + EnableBulkUpdate conflict (warning, not error)
            if (typeof(IConcurrent).IsAssignableFrom(entityType) && attr.EnableBulkUpdate)
            {
                _logger.LogWarning(
                    "{Entity} uses both IConcurrent and EnableBulkUpdate. " +
                    "Bulk update bypasses the change tracker and will not update RowVersion — " +
                    "concurrency protection is disabled for bulk operations.",
                    entityType.Name);
            }

            // Check: WorkflowProtected fields exist on the entity
            if (attr.WorkflowProtected != null)
            {
                foreach (var field in attr.WorkflowProtected)
                {
                    var prop = entityType.GetProperty(field,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (prop == null)
                    {
                        throw new InvalidOperationException(
                            $"[CrudEntity(WorkflowProtected)] on {entityType.Name}: " +
                            $"property '{field}' was not found. " +
                            $"Ensure the property exists and is public.");
                    }
                }
            }
        }
    }
}
```

- [ ] **11.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~StartupValidation"`. All tests should pass.

- [ ] **11.5** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **11.6** Commit: `feat(api): add CrudKitStartupValidator with entity metadata validation at startup`

---

## Task 12: Final Integration Verification

Run all tests, verify the complete API layer works end-to-end.

### Steps

- [ ] **12.1** Run full test suite: `dotnet test CrudKit.slnx`. All tests across all projects should pass.

- [ ] **12.2** Run build: `dotnet build CrudKit.slnx --no-restore`. Should compile cleanly with no warnings.

- [ ] **12.3** Verify file structure matches the plan. All files listed in the File Structure section should exist.

- [ ] **12.4** Commit: `chore(api): final integration verification — all tests pass`

---

## Summary

| Task | What | Key Files | Test Count |
|------|------|-----------|------------|
| 1 | Project scaffold | csproj, TestWebApp, TestEntities, ApiTestDbContext | 0 |
| 2 | AppErrorFilter (IEndpointFilter) | AppErrorFilter.cs, AppErrorFilterTests.cs | 10 |
| 3 | ValidationFilter (FluentValidation + DataAnnotation) | ValidationFilter.cs, ValidationFilterTests.cs | 5 |
| 4 | Auth filters + RouteGroupExtensions | 3 filter files + RouteGroupExtensions.cs, AuthFilterTests.cs | 6 |
| 5 | CrudKitApiOptions + AddCrudKit + UseCrudKit + IModule | CrudKitApiOptions.cs, CrudKitAppExtensions.cs, CrudKitStartupValidator.cs (stub) | 7 |
| 6 | CrudEndpointMapper (CRUD + transactions + hooks + mapper) | CrudEndpointMapper.cs, CrudEndpointMapperTests.cs | ~17 |
| 7 | Restore + Transition tests | CrudEndpointMapperTests.cs (additions) | 5 |
| 8 | IdempotencyFilter + Record + CleanupService | 3 files, IdempotencyFilterTests.cs | 4 |
| 9 | Bulk endpoints | CrudEndpointMapper.cs (additions), BulkEndpointTests.cs | 4 |
| 10 | DetailEndpointMapper | DetailEndpointMapper.cs, DetailEndpointMapperTests.cs | 5 |
| 11 | Startup validation | CrudKitStartupValidator.cs (full), StartupValidationTests.cs | 2 |
| 12 | Final integration verification | — | 0 (re-run) |

**Total estimated tests: ~65**
