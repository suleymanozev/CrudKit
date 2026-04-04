# CrudKit.Api Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the CrudKit.Api layer — Minimal API endpoint mapping, validation, auth filters, hooks integration, and DI wiring that connects CrudKit.Core and CrudKit.EntityFrameworkCore into a production-ready API framework.

**Architecture:** `CrudEndpointMapper.MapCrudEndpoints<TEntity, TCreate, TUpdate>()` auto-maps 5+ endpoints per entity using inline Minimal API lambdas with generic type parameters. `AppErrorFilter` (IMiddleware) converts `AppError` exceptions to ProblemDetails. `AddCrudKit<TContext>()` is a single-call DI setup that layers on top of `AddCrudKitEf<TContext>()`. All cross-cutting behaviors (soft-delete restore, state machine transitions, lifecycle hooks) are handled transparently.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API (Microsoft.AspNetCore.App framework reference), Microsoft.AspNetCore.Mvc.Testing (integration tests), Microsoft.EntityFrameworkCore.Sqlite 10.* (tests), xUnit 2.*

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
│   ├── RequireAuthFilter.cs
│   ├── RequireRoleFilter.cs
│   └── RequirePermissionFilter.cs
├── Middleware/
│   └── RequestLoggingMiddleware.cs
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
│   └── AuthFilterTests.cs
├── Endpoints/
│   ├── CrudEndpointMapperTests.cs
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

namespace CrudKit.Api.Tests.Helpers;

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

// For DetailEndpointMapper tests
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

public class CreateInvoiceDto { public string Title { get; set; } = string.Empty; }
public class UpdateInvoiceDto { public string? Title { get; set; } }
public class CreateInvoiceLineDto
{
    public string InvoiceId { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
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
    public static async Task<TestWebApp> CreateAsync(
        ICurrentUser? currentUser = null,
        Action<WebApplication>? configureEndpoints = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

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

## Task 2: AppErrorFilter

Middleware that catches `AppError` exceptions and returns structured JSON error responses.

### Steps

- [ ] **2.1** Create test file `tests/CrudKit.Api.Tests/Filters/AppErrorFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Core.Models;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class AppErrorFilterTests
{
    [Fact]
    public async Task ThrowsAppErrorNotFound_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-not-found", () => throw AppError.NotFound("Item not found")));

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
            a.MapGet("/test-bad", () => throw AppError.BadRequest("Bad input")));

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
            a.MapGet("/test-auth", () => throw AppError.Unauthorized()));

        var response = await app.Client.GetAsync("/test-auth");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ThrowsAppErrorForbidden_Returns403()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-forbidden", () => throw AppError.Forbidden()));

        var response = await app.Client.GetAsync("/test-forbidden");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ThrowsAppErrorConflict_Returns409()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/test-conflict", () => throw AppError.Conflict("Already exists")));

        var response = await app.Client.GetAsync("/test-conflict");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task NormalResponse_PassesThrough()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapGet("/ok", () => Results.Ok(new { msg = "hello" })));

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

namespace CrudKit.Api.Filters;

/// <summary>
/// Middleware that catches AppError exceptions and converts them to HTTP ProblemDetails responses.
/// Registered as IMiddleware so it participates in DI (scoped lifetime).
/// Register via app.UseMiddleware&lt;AppErrorFilter&gt;() or app.UseCrudKit().
/// </summary>
public class AppErrorFilter : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (AppError ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                status = ex.StatusCode,
                code = ex.Code,
                message = ex.Message,
                details = ex.Details
            });
        }
    }
}
```

- [ ] **2.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AppErrorFilterTests"`. All 6 tests should pass.

- [ ] **2.5** Commit: `feat(api): add AppErrorFilter middleware for AppError-to-HTTP conversion`

---

## Task 3: ValidationFilter

Endpoint filter that validates request DTOs using `System.ComponentModel.DataAnnotations` and throws `AppError.Validation` on failure.

### Steps

- [ ] **3.1** Create test file `tests/CrudKit.Api.Tests/Filters/ValidationFilterTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class ValidationFilterTests
{
    [Fact]
    public async Task MissingRequiredField_Returns400WithValidationCode()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        // Name is missing — [Required] should fail
        var response = await app.Client.PostAsJsonAsync("/api/products", new { Price = 9.99 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RangeViolation_Returns400()
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
}
```

- [ ] **3.2** Verify tests fail (build error — `ValidationFilter` and `CrudEndpointMapper` do not exist yet). This is expected; these tests will compile and pass only after Task 3.3 + Task 6.

- [ ] **3.3** Create `src/CrudKit.Api/Filters/ValidationFilter.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Models;
using Microsoft.AspNetCore.Http;

namespace CrudKit.Api.Filters;

/// <summary>
/// Endpoint filter that validates the request body using DataAnnotations.
/// Automatically added to POST and PUT endpoints by MapCrudEndpoints.
/// Throws AppError.Validation (caught by AppErrorFilter) on invalid input.
/// </summary>
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (arg == null) return await next(ctx);

        var validationCtx = new ValidationContext(arg);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(arg, validationCtx, results, validateAllProperties: true))
        {
            var errors = new ValidationErrors();
            foreach (var result in results)
            {
                var field = result.MemberNames.FirstOrDefault() ?? "unknown";
                errors.Add(field, "INVALID", result.ErrorMessage ?? "Invalid value.");
            }
            throw AppError.Validation(errors);
        }

        return await next(ctx);
    }
}
```

- [ ] **3.4** Note: ValidationFilter tests depend on `CrudEndpointMapper` which is built in Task 6. These tests will be verified in Task 6 after the endpoint mapper is created.

- [ ] **3.5** Commit: `feat(api): add ValidationFilter for DataAnnotations-based DTO validation`

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
        // FakeCurrentUser defaults to IsAuthenticated = true
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
        // FakeCurrentUser with Roles = ["user"], filter requires "admin"
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
        // FakeCurrentUser defaults to Roles = ["admin"]
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
        // AnonymousCurrentUser.HasPermission always returns false
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
        // FakeCurrentUser.HasPermission always returns true
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
            return Results.Problem(statusCode: 401, title: "Unauthorized");
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
            return Results.Problem(statusCode: 403, title: "Forbidden");
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
            return Results.Problem(statusCode: 403, title: "Forbidden");
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

## Task 5: CrudKitApiOptions + CrudKitAppExtensions + RequestLoggingMiddleware

DI registration entry point, configuration options, and request logging.

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
}
```

- [ ] **5.2** Create `src/CrudKit.Api/Middleware/RequestLoggingMiddleware.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Middleware;

/// <summary>
/// Logs each HTTP request: method, path, and response status code.
/// Registered as IMiddleware (scoped) to participate in DI.
/// </summary>
public class RequestLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) => _logger = logger;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);
        _logger.LogInformation("{Method} {Path} -> {StatusCode}",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode);
    }
}
```

- [ ] **5.3** Create `src/CrudKit.Api/Extensions/CrudKitAppExtensions.cs`:

```csharp
using CrudKit.Api.Configuration;
using CrudKit.Api.Filters;
using CrudKit.Api.Middleware;
using CrudKit.Core.Auth;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    /// <example>
    /// services.AddDbContext&lt;AppDbContext&gt;(...);
    /// services.AddCrudKit&lt;AppDbContext&gt;(opts =&gt; opts.DefaultPageSize = 50);
    /// </example>
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

        // AppErrorFilter is IMiddleware — must be registered in DI
        services.TryAddScoped<AppErrorFilter>();

        // RequestLoggingMiddleware is IMiddleware — must be registered in DI
        services.TryAddScoped<RequestLoggingMiddleware>();

        // Anonymous fallback for ICurrentUser (if not already registered)
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        // Module scan — discover and register all IModule implementations in the given assembly
        if (opts.ScanModulesFromAssembly != null)
        {
            var moduleTypes = opts.ScanModulesFromAssembly
                .GetTypes()
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

            foreach (var moduleType in moduleTypes)
                services.AddSingleton(typeof(IModule), moduleType);
        }

        return services;
    }

    /// <summary>
    /// Activates CrudKit middleware and maps all registered module endpoints.
    /// Call after app = builder.Build().
    /// </summary>
    public static WebApplication UseCrudKit(this WebApplication app)
    {
        app.UseMiddleware<AppErrorFilter>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        // Map endpoints for all registered modules
        foreach (var module in app.Services.GetServices<IModule>())
            module.MapEndpoints(app);

        return app;
    }

    /// <summary>
    /// Registers a single module manually without assembly scan.
    /// </summary>
    public static IServiceCollection AddCrudKitModule<TModule>(
        this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        services.AddSingleton<IModule, TModule>();
        return services;
    }
}
```

- [ ] **5.4** Delete `src/CrudKit.Api/Placeholder.cs` (no longer needed — real types exist now).

- [ ] **5.5** Create test file `tests/CrudKit.Api.Tests/Extensions/CrudKitAppExtensionsTests.cs`:

```csharp
using CrudKit.Api.Configuration;
using CrudKit.Api.Extensions;
using CrudKit.Api.Filters;
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
    public void AddCrudKit_RegistersAppErrorFilter()
    {
        using var sp = (ServiceProvider)BuildProvider();
        using var scope = sp.CreateScope();
        var filter = scope.ServiceProvider.GetRequiredService<AppErrorFilter>();
        Assert.NotNull(filter);
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
        });

        using var sp = (ServiceProvider)services.BuildServiceProvider();
        var opts = sp.GetRequiredService<CrudKitApiOptions>();
        Assert.Equal(50, opts.DefaultPageSize);
        Assert.Equal(200, opts.MaxPageSize);
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

    private class TestModule : IModule
    {
        public string Name => "Test";
        public void RegisterServices(IServiceCollection services, IConfiguration config) { }
        public void MapEndpoints(WebApplication app) { }
    }
}
```

- [ ] **5.6** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudKitAppExtensionsTests"`. All 7 tests should pass.

- [ ] **5.7** Also re-run previously created tests to ensure nothing broke: `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AppErrorFilterTests|FullyQualifiedName~AuthFilterTests"`. All should pass.

- [ ] **5.8** Commit: `feat(api): add CrudKitApiOptions, CrudKitAppExtensions, RequestLoggingMiddleware`

---

## Task 6: CrudEndpointMapper — List, Get, Create, Update, Delete

Maps the 5 standard CRUD endpoints for any entity type using Minimal API lambdas.

### Steps

- [ ] **6.1** Create test file `tests/CrudKit.Api.Tests/Endpoints/CrudEndpointMapperTests.cs` (basic CRUD only — no Restore/Transition yet):

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
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
}
```

- [ ] **6.2** Verify tests fail (build error — `CrudEndpointMapper` does not exist yet).

- [ ] **6.3** Create `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs` with the 5 basic CRUD endpoints (no Restore/Transition/Hooks yet — those come in Task 7 and 8):

```csharp
using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps standard CRUD endpoints for an entity using ASP.NET Core Minimal API.
/// </summary>
public static class CrudEndpointMapper
{
    /// <summary>
    /// Maps standard CRUD endpoints for an entity: GET /, GET /{id}, POST /, PUT /{id}, DELETE /{id}.
    /// Soft-delete restore and state machine transitions are added conditionally.
    /// </summary>
    public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app,
        string route)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var group = app.MapGroup($"/api/{route}");

        // ---- List ----
        group.MapGet("/", async (
            HttpContext ctx,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(ctx.Request.Query);
            var result = await repo.List(listParams, ct);
            return Results.Ok(result);
        });

        // ---- Get by id ----
        group.MapGet("/{id}", async (
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.FindByIdOrDefault(id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        });

        // ---- Create ----
        group.MapPost("/", async (
            [FromBody] TCreate body,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.Create(body, ct);
            return Results.Created($"/api/{route}/{entity.Id}", entity);
        }).AddEndpointFilter<ValidationFilter<TCreate>>();

        // ---- Update ----
        group.MapPut("/{id}", async (
            string id,
            [FromBody] TUpdate body,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.Update(id, body, ct);
            return Results.Ok(entity);
        }).AddEndpointFilter<ValidationFilter<TUpdate>>();

        // ---- Delete ----
        group.MapDelete("/{id}", async (
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            await repo.Delete(id, ct);
            return Results.NoContent();
        });

        return group;
    }
}
```

- [ ] **6.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudEndpointMapperTests"`. All 9 tests should pass.

- [ ] **6.5** Also verify ValidationFilter tests now pass: `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~ValidationFilterTests"`. All 3 tests should pass.

- [ ] **6.6** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **6.7** Commit: `feat(api): add CrudEndpointMapper with List, Get, Create, Update, Delete endpoints`

---

## Task 7: CrudEndpointMapper — Restore + Transition

Extend `CrudEndpointMapper` with conditional endpoints for `ISoftDeletable` (restore) and `IStateMachine<TState>` (transition).

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
        // OrderStatus.Processing = 1
        Assert.Equal(1, body.GetProperty("status").GetInt32());
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
        // OrderStatus.Completed = 2
        Assert.Equal(2, body.GetProperty("status").GetInt32());
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

- [ ] **7.2** Verify new tests fail (Restore and Transition endpoints are not mapped yet).

- [ ] **7.3** Update `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs` — add Restore and Transition endpoints **after** the Delete endpoint, still inside `MapCrudEndpoints`. Replace the entire file with:

```csharp
using System.Reflection;
using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps standard CRUD endpoints for an entity using ASP.NET Core Minimal API.
/// Conditionally adds Restore (ISoftDeletable) and Transition (IStateMachine) endpoints.
/// </summary>
public static class CrudEndpointMapper
{
    /// <summary>
    /// Maps standard CRUD endpoints for an entity: GET /, GET /{id}, POST /, PUT /{id}, DELETE /{id}.
    /// If TEntity implements ISoftDeletable, POST /{id}/restore is also added.
    /// If TEntity implements IStateMachine&lt;TState&gt;, POST /{id}/transition/{action} is also added.
    /// </summary>
    public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app,
        string route)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var group = app.MapGroup($"/api/{route}");

        // ---- List ----
        group.MapGet("/", async (
            HttpContext ctx,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(ctx.Request.Query);
            var result = await repo.List(listParams, ct);
            return Results.Ok(result);
        });

        // ---- Get by id ----
        group.MapGet("/{id}", async (
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.FindByIdOrDefault(id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        });

        // ---- Create ----
        group.MapPost("/", async (
            [FromBody] TCreate body,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.Create(body, ct);
            return Results.Created($"/api/{route}/{entity.Id}", entity);
        }).AddEndpointFilter<ValidationFilter<TCreate>>();

        // ---- Update ----
        group.MapPut("/{id}", async (
            string id,
            [FromBody] TUpdate body,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.Update(id, body, ct);
            return Results.Ok(entity);
        }).AddEndpointFilter<ValidationFilter<TUpdate>>();

        // ---- Delete ----
        group.MapDelete("/{id}", async (
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            await repo.Delete(id, ct);
            return Results.NoContent();
        });

        // ---- Restore (ISoftDeletable only) ----
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            group.MapPost("/{id}/restore", async (
                string id,
                IRepo<TEntity> repo,
                CancellationToken ct) =>
            {
                await repo.Restore(id, ct);
                return Results.Ok();
            });
        }

        // ---- Transition (IStateMachine<TState> only) ----
        var smInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachine<>));

        if (smInterface != null)
        {
            group.MapPost("/{id}/transition/{action}", async (
                string id,
                string action,
                IRepo<TEntity> repo,
                CancellationToken ct) =>
            {
                var entity = await repo.FindById(id, ct);

                // Get the static Transitions property
                var transitionsProp = typeof(TEntity).GetProperty(
                    "Transitions", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (transitionsProp == null)
                    return Results.Problem(statusCode: 500, title: "No Transitions property found.");

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
                    return Results.BadRequest(new { error = $"Transition '{action}' is not valid from the current state." });

                // Apply the transition
                statusProp.SetValue(entity, newStatus);
                var updated = await repo.Update(id, entity, ct);
                return Results.Ok(updated);
            });
        }

        return group;
    }
}
```

- [ ] **7.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudEndpointMapperTests"`. All tests (basic CRUD + Restore + Transition) should pass.

- [ ] **7.5** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **7.6** Commit: `feat(api): add Restore and Transition endpoints to CrudEndpointMapper`

---

## Task 8: ICrudHooks Integration

Extend `CrudEndpointMapper` to call `ICrudHooks<TEntity>` lifecycle hooks in Create, Update, Delete, and Restore handlers.

### Steps

- [ ] **8.1** Add a test helper class and hooks tests to `tests/CrudKit.Api.Tests/Endpoints/CrudEndpointMapperTests.cs`. Append the following at the bottom of the file, **outside** the `CrudEndpointMapperTests` class:

```csharp
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
```

Then add the following test methods **inside** the `CrudEndpointMapperTests` class:

```csharp
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
        // No ICrudHooks<ProductEntity> registered — should work without hooks
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: a =>
            a.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products"));

        var response = await app.Client.PostAsJsonAsync("/api/products",
            new { Name = "NoHooks", Price = 5.00 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
```

Add the required using at the top of the test file:

```csharp
using CrudKit.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **8.2** Verify new hooks tests fail (hooks are not called in the current implementation).

- [ ] **8.3** Update `src/CrudKit.Api/Endpoints/CrudEndpointMapper.cs` — modify Create, Update, Delete, and Restore handlers to resolve and call `ICrudHooks<TEntity>`. Replace the entire file with:

```csharp
using System.Reflection;
using CrudKit.Api.Filters;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps standard CRUD endpoints for an entity using ASP.NET Core Minimal API.
/// Conditionally adds Restore (ISoftDeletable) and Transition (IStateMachine) endpoints.
/// Calls ICrudHooks&lt;TEntity&gt; lifecycle hooks when registered in DI.
/// </summary>
public static class CrudEndpointMapper
{
    /// <summary>
    /// Maps standard CRUD endpoints for an entity: GET /, GET /{id}, POST /, PUT /{id}, DELETE /{id}.
    /// If TEntity implements ISoftDeletable, POST /{id}/restore is also added.
    /// If TEntity implements IStateMachine&lt;TState&gt;, POST /{id}/transition/{action} is also added.
    /// ICrudHooks&lt;TEntity&gt; lifecycle hooks are called if registered in DI.
    /// </summary>
    public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app,
        string route)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var group = app.MapGroup($"/api/{route}");

        // ---- List ----
        group.MapGet("/", async (
            HttpContext ctx,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var listParams = ListParams.FromQuery(ctx.Request.Query);
            var result = await repo.List(listParams, ct);
            return Results.Ok(result);
        });

        // ---- Get by id ----
        group.MapGet("/{id}", async (
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var entity = await repo.FindByIdOrDefault(id, ct);
            return entity is null ? Results.NotFound() : Results.Ok(entity);
        });

        // ---- Create ----
        group.MapPost("/", async (
            HttpContext httpCtx,
            [FromBody] TCreate body,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
            var appCtx = new Core.Context.AppContext
            {
                Services = httpCtx.RequestServices,
                CurrentUser = currentUser
            };

            if (hooks != null)
            {
                // Shell entity for auth checks and early validation in BeforeCreate
                var shell = Activator.CreateInstance<TEntity>();
                await hooks.BeforeCreate(shell, appCtx);
            }

            var entity = await repo.Create(body, ct);

            if (hooks != null)
                await hooks.AfterCreate(entity, appCtx);

            return Results.Created($"/api/{route}/{entity.Id}", entity);
        }).AddEndpointFilter<ValidationFilter<TCreate>>();

        // ---- Update ----
        group.MapPut("/{id}", async (
            HttpContext httpCtx,
            string id,
            [FromBody] TUpdate body,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
            var appCtx = new Core.Context.AppContext
            {
                Services = httpCtx.RequestServices,
                CurrentUser = currentUser
            };

            if (hooks != null)
            {
                var existing = await repo.FindById(id, ct);
                await hooks.BeforeUpdate(existing, appCtx);
            }

            var entity = await repo.Update(id, body, ct);

            if (hooks != null)
                await hooks.AfterUpdate(entity, appCtx);

            return Results.Ok(entity);
        }).AddEndpointFilter<ValidationFilter<TUpdate>>();

        // ---- Delete ----
        group.MapDelete("/{id}", async (
            HttpContext httpCtx,
            string id,
            IRepo<TEntity> repo,
            CancellationToken ct) =>
        {
            var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
            var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
            var appCtx = new Core.Context.AppContext
            {
                Services = httpCtx.RequestServices,
                CurrentUser = currentUser
            };

            TEntity? entity = null;

            if (hooks != null)
            {
                entity = await repo.FindById(id, ct);
                await hooks.BeforeDelete(entity, appCtx);
            }

            await repo.Delete(id, ct);

            if (hooks != null && entity != null)
                await hooks.AfterDelete(entity, appCtx);

            return Results.NoContent();
        });

        // ---- Restore (ISoftDeletable only) ----
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        {
            group.MapPost("/{id}/restore", async (
                HttpContext httpCtx,
                string id,
                IRepo<TEntity> repo,
                CancellationToken ct) =>
            {
                var hooks = httpCtx.RequestServices.GetService<ICrudHooks<TEntity>>();
                var currentUser = httpCtx.RequestServices.GetRequiredService<ICurrentUser>();
                var appCtx = new Core.Context.AppContext
                {
                    Services = httpCtx.RequestServices,
                    CurrentUser = currentUser
                };

                if (hooks != null)
                {
                    var entity = await repo.FindById(id, ct);
                    await hooks.BeforeRestore(entity, appCtx);
                }

                await repo.Restore(id, ct);

                if (hooks != null)
                {
                    var restored = await repo.FindById(id, ct);
                    await hooks.AfterRestore(restored, appCtx);
                }

                return Results.Ok();
            });
        }

        // ---- Transition (IStateMachine<TState> only) ----
        var smInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachine<>));

        if (smInterface != null)
        {
            group.MapPost("/{id}/transition/{action}", async (
                string id,
                string action,
                IRepo<TEntity> repo,
                CancellationToken ct) =>
            {
                var entity = await repo.FindById(id, ct);

                // Get the static Transitions property
                var transitionsProp = typeof(TEntity).GetProperty(
                    "Transitions", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (transitionsProp == null)
                    return Results.Problem(statusCode: 500, title: "No Transitions property found.");

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
                    return Results.BadRequest(new { error = $"Transition '{action}' is not valid from the current state." });

                // Apply the transition
                statusProp.SetValue(entity, newStatus);
                var updated = await repo.Update(id, entity, ct);
                return Results.Ok(updated);
            });
        }

        return group;
    }
}
```

- [ ] **8.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudEndpointMapperTests"`. All tests (basic CRUD + Restore + Transition + Hooks) should pass.

- [ ] **8.5** Run full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **8.6** Commit: `feat(api): integrate ICrudHooks lifecycle hooks into CrudEndpointMapper`

---

## Task 9: DetailEndpointMapper

Maps nested CRUD endpoints for master-detail relationships (e.g., Invoice -> InvoiceLines).

### Steps

- [ ] **9.1** Create test file `tests/CrudKit.Api.Tests/Endpoints/DetailEndpointMapperTests.cs`:

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
    private static Task<TestWebApp> CreateAppAsync()
        => TestWebApp.CreateAsync(configureEndpoints: app =>
        {
            app.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices");
            app.MapCrudDetailEndpoints<InvoiceEntity, InvoiceLineEntity, CreateInvoiceLineDto>(
                "invoices", "lines", "InvoiceId");
        });

    [Fact]
    public async Task ListDetails_ReturnsEmptyForNewMaster()
    {
        await using var app = await CreateAppAsync();

        var createInvoice = await app.Client.PostAsJsonAsync("/api/invoices",
            new { Title = "INV-001" });
        var invoice = await createInvoice.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task CreateDetail_AddsLineToInvoice()
    {
        await using var app = await CreateAppAsync();

        var createInvoice = await app.Client.PostAsJsonAsync("/api/invoices",
            new { Title = "INV-002" });
        var invoice = await createInvoice.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetString()!;

        var createLine = await app.Client.PostAsJsonAsync(
            $"/api/invoices/{invoiceId}/lines",
            new { Description = "Item 1", Amount = 100.00 });

        Assert.Equal(HttpStatusCode.Created, createLine.StatusCode);
        var line = await createLine.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Item 1", line.GetProperty("description").GetString());
    }

    [Fact]
    public async Task CreateDetail_SetsForeignKeyAutomatically()
    {
        await using var app = await CreateAppAsync();

        var createInvoice = await app.Client.PostAsJsonAsync("/api/invoices",
            new { Title = "INV-FK" });
        var invoice = await createInvoice.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetString()!;

        var createLine = await app.Client.PostAsJsonAsync(
            $"/api/invoices/{invoiceId}/lines",
            new { Description = "FK test", Amount = 50.00 });

        var line = await createLine.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(invoiceId, line.GetProperty("invoiceId").GetString());
    }

    [Fact]
    public async Task GetDetail_ReturnsSpecificLine()
    {
        await using var app = await CreateAppAsync();

        var createInvoice = await app.Client.PostAsJsonAsync("/api/invoices",
            new { Title = "INV-GET" });
        var invoice = await createInvoice.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetString()!;

        var createLine = await app.Client.PostAsJsonAsync(
            $"/api/invoices/{invoiceId}/lines",
            new { Description = "Specific line", Amount = 75.00 });
        var line = await createLine.Content.ReadFromJsonAsync<JsonElement>();
        var lineId = line.GetProperty("id").GetString()!;

        var response = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Specific line", body.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ListDetails_Returns404ForMissingMaster()
    {
        await using var app = await CreateAppAsync();
        var response = await app.Client.GetAsync("/api/invoices/nonexistent/lines");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDetail_Returns204()
    {
        await using var app = await CreateAppAsync();

        var createInvoice = await app.Client.PostAsJsonAsync("/api/invoices",
            new { Title = "INV-DEL" });
        var invoice = await createInvoice.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetString()!;

        var createLine = await app.Client.PostAsJsonAsync(
            $"/api/invoices/{invoiceId}/lines",
            new { Description = "To delete", Amount = 10.00 });
        var line = await createLine.Content.ReadFromJsonAsync<JsonElement>();
        var lineId = line.GetProperty("id").GetString()!;

        var delete = await app.Client.DeleteAsync($"/api/invoices/{invoiceId}/lines/{lineId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task BatchUpsert_ReplacesAllDetails()
    {
        await using var app = await CreateAppAsync();

        var createInvoice = await app.Client.PostAsJsonAsync("/api/invoices",
            new { Title = "INV-BATCH" });
        var invoice = await createInvoice.Content.ReadFromJsonAsync<JsonElement>();
        var invoiceId = invoice.GetProperty("id").GetString()!;

        // Create initial line
        await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { Description = "Old line", Amount = 50.00 });

        // Batch replace
        var batch = await app.Client.PutAsJsonAsync($"/api/invoices/{invoiceId}/lines/batch",
            new[] {
                new { Description = "New line 1", Amount = 25.00 },
                new { Description = "New line 2", Amount = 75.00 }
            });

        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        var result = await batch.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetArrayLength());

        // Verify old line was replaced
        var lines = await app.Client.GetAsync($"/api/invoices/{invoiceId}/lines");
        var linesBody = await lines.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, linesBody.GetArrayLength());
    }

    [Fact]
    public async Task BatchUpsert_Returns404ForMissingMaster()
    {
        await using var app = await CreateAppAsync();

        var batch = await app.Client.PutAsJsonAsync("/api/invoices/nonexistent/lines/batch",
            new[] { new { Description = "Line", Amount = 10.00 } });

        Assert.Equal(HttpStatusCode.NotFound, batch.StatusCode);
    }
}
```

- [ ] **9.2** Verify tests fail (build error — `DetailEndpointMapper` does not exist yet).

- [ ] **9.3** Create `src/CrudKit.Api/Endpoints/DetailEndpointMapper.cs`:

```csharp
using System.Reflection;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Maps nested CRUD endpoints for a master-detail relationship.
/// The detail entity has a foreign key property pointing back to the master.
/// </summary>
public static class DetailEndpointMapper
{
    /// <summary>
    /// Maps nested CRUD endpoints for a master-detail relationship.
    /// Routes: GET /api/{master}/{masterId}/{detail}
    ///         GET /api/{master}/{masterId}/{detail}/{id}
    ///         POST /api/{master}/{masterId}/{detail}
    ///         PUT /api/{master}/{masterId}/{detail}/batch
    ///         DELETE /api/{master}/{masterId}/{detail}/{id}
    /// </summary>
    /// <param name="app">The WebApplication instance.</param>
    /// <param name="masterRoute">URL segment for the master entity (e.g., "invoices").</param>
    /// <param name="detailRoute">URL segment for the detail entity (e.g., "lines").</param>
    /// <param name="foreignKeyProperty">Name of the FK property on TDetail/TCreateDetail (e.g., "InvoiceId").</param>
    public static RouteGroupBuilder MapCrudDetailEndpoints<TMaster, TDetail, TCreateDetail>(
        this WebApplication app,
        string masterRoute,
        string detailRoute,
        string foreignKeyProperty)
        where TMaster : class, IEntity
        where TDetail : class, IEntity
        where TCreateDetail : class
    {
        var group = app.MapGroup($"/api/{masterRoute}/{{masterId}}/{detailRoute}");

        // ---- List details by master ----
        group.MapGet("/", async (
            string masterId,
            IRepo<TMaster> masterRepo,
            IRepo<TDetail> detailRepo,
            CancellationToken ct) =>
        {
            // Verify master exists
            var master = await masterRepo.FindByIdOrDefault(masterId, ct);
            if (master is null) return Results.NotFound();

            var details = await detailRepo.FindByField(foreignKeyProperty, masterId, ct);
            return Results.Ok(details);
        });

        // ---- Get detail by id ----
        group.MapGet("/{id}", async (
            string masterId,
            string id,
            IRepo<TMaster> masterRepo,
            IRepo<TDetail> detailRepo,
            CancellationToken ct) =>
        {
            var master = await masterRepo.FindByIdOrDefault(masterId, ct);
            if (master is null) return Results.NotFound();

            var detail = await detailRepo.FindByIdOrDefault(id, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        // ---- Create detail ----
        group.MapPost("/", async (
            string masterId,
            [FromBody] TCreateDetail body,
            IRepo<TMaster> masterRepo,
            IRepo<TDetail> detailRepo,
            CancellationToken ct) =>
        {
            var master = await masterRepo.FindByIdOrDefault(masterId, ct);
            if (master is null) return Results.NotFound();

            // Set foreign key on the DTO via reflection
            var fkProp = body.GetType().GetProperty(foreignKeyProperty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            fkProp?.SetValue(body, masterId);

            var detail = await detailRepo.Create(body, ct);
            return Results.Created($"/api/{masterRoute}/{masterId}/{detailRoute}/{detail.Id}", detail);
        });

        // ---- Delete detail ----
        group.MapDelete("/{id}", async (
            string masterId,
            string id,
            IRepo<TMaster> masterRepo,
            IRepo<TDetail> detailRepo,
            CancellationToken ct) =>
        {
            var master = await masterRepo.FindByIdOrDefault(masterId, ct);
            if (master is null) return Results.NotFound();

            await detailRepo.Delete(id, ct);
            return Results.NoContent();
        });

        // ---- Batch upsert (replace all details for this master) ----
        group.MapPut("/batch", async (
            string masterId,
            [FromBody] List<TCreateDetail> items,
            IRepo<TMaster> masterRepo,
            IRepo<TDetail> detailRepo,
            CancellationToken ct) =>
        {
            var master = await masterRepo.FindByIdOrDefault(masterId, ct);
            if (master is null) return Results.NotFound();

            // Delete existing details for this master
            var existing = await detailRepo.FindByField(foreignKeyProperty, masterId, ct);
            foreach (var item in existing)
                await detailRepo.Delete(item.Id, ct);

            // Create new details
            var result = new List<TDetail>();
            foreach (var item in items)
            {
                var fkProp = item.GetType().GetProperty(foreignKeyProperty,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                fkProp?.SetValue(item, masterId);
                var detail = await detailRepo.Create(item, ct);
                result.Add(detail);
            }

            return Results.Ok(result);
        });

        return group;
    }
}
```

- [ ] **9.4** Verify: Run `dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~DetailEndpointMapperTests"`. All 9 tests should pass.

- [ ] **9.5** Run the full test suite: `dotnet test tests/CrudKit.Api.Tests/`. All tests should pass.

- [ ] **9.6** Run `dotnet build CrudKit.slnx` to confirm the entire solution compiles cleanly.

- [ ] **9.7** Commit: `feat(api): add DetailEndpointMapper for master-detail nested endpoints`

---

## Summary

| Task | What | Files | Tests |
|------|------|-------|-------|
| 1 | Project scaffold | csproj, slnx, test helpers | Build only |
| 2 | AppErrorFilter | 1 source, 1 test | 6 tests |
| 3 | ValidationFilter | 1 source, 1 test | 3 tests |
| 4 | Auth filters + RouteGroupExtensions | 4 source, 1 test | 6 tests |
| 5 | CrudKitApiOptions + AppExtensions + Logging | 3 source, 1 test | 7 tests |
| 6 | CrudEndpointMapper (basic CRUD) | 1 source, 1 test | 9 tests |
| 7 | CrudEndpointMapper (Restore + Transition) | 1 source (extend), 1 test (extend) | +6 tests |
| 8 | ICrudHooks integration | 1 source (extend), 1 test (extend) | +4 tests |
| 9 | DetailEndpointMapper | 1 source, 1 test | 9 tests |

**Total: 9 tasks, 12 source files, 7 test files, ~50 tests**

### Run Commands Reference
```bash
# Build entire solution
dotnet build CrudKit.slnx

# Run all CrudKit.Api tests
dotnet test tests/CrudKit.Api.Tests/

# Run specific test class
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AppErrorFilterTests"
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~ValidationFilterTests"
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~AuthFilterTests"
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudKitAppExtensionsTests"
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~CrudEndpointMapperTests"
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~DetailEndpointMapperTests"

# Run all tests in the solution
dotnet test CrudKit.slnx
```
