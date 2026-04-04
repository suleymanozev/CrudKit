## 3. Auth Yaklaşımı

CrudKit kimlik doğrulama implementasyonu sağlamaz. Ayrı bir `CrudKit.Auth` paketi yoktur.

Auth tamamen kullanıcının sorumluluğundadır (JWT, OAuth, Keycloak, Auth0, Azure AD, cookie, vb.). CrudKit sadece `ICurrentUser` interface'i üzerinden mevcut kullanıcı bilgisine erişir. Bu interface CrudKit.Core'da tanımlıdır.

- `ICurrentUser` interface tanımı, implementasyon örnekleri, endpoint koruma filtreleri ve testler → **Bölüm 10**
- `Permission` ve `PermScope` modelleri → **Bölüm 1.4** (CrudKit.Core Models)

---


## 10. ICurrentUser — Auth Soyutlaması

CrudKit kimlik doğrulama işine karışmaz. JWT, OAuth, Cookie, Windows Auth, custom token — ne kullanılırsa kullanılsın CrudKit'i ilgilendirmez. CrudKit sadece "şu an kim işlem yapıyor" bilgisine ihtiyaç duyar. Bu bilgi `ICurrentUser` interface'i ile dışarıdan sağlanır.

### 10.1 Neden Bu Yaklaşım?

```
YANLIŞ — CrudKit auth yapıyor:
  CrudKit.Auth paketi JWT üretiyor, OAuth flow yönetiyor,
  token doğruluyor, refresh token saklıyor...
  → Bakım yükü, güvenlik riski, tekrar eden iş

DOĞRU — CrudKit auth'u soyutluyor:
  Kullanıcı kendi auth'unu yapar (Keycloak, Auth0, IdentityServer, custom)
  CrudKit sadece ICurrentUser interface'ini kullanır
  → Tek sorumluluk, sıfır auth bağımlılığı
```

### 10.2 ICurrentUser Interface (CrudKit.Core'da tanımlı)

```csharp
/// <summary>
/// Mevcut oturumdaki kullanıcı bilgisi.
/// Bu interface'i uygulama tarafı implemente eder.
/// CrudKit sadece bu interface üzerinden kullanıcı bilgisine erişir.
/// </summary>
public interface ICurrentUser
{
    /// <summary>Kullanıcı ID'si. Auth yoksa null.</summary>
    string? Id { get; }

    /// <summary>Kullanıcı adı.</summary>
    string? Username { get; }

    /// <summary>Tenant ID'si. Multi-tenant değilse null.</summary>
    string? TenantId { get; }

    /// <summary>Kullanıcının rolleri.</summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>Kullanıcının izinleri.</summary>
    IReadOnlyList<Permission> Permissions { get; }

    /// <summary>Kullanıcı authenticate olmuş mu?</summary>
    bool IsAuthenticated { get; }

    /// <summary>Belirli bir role sahip mi?</summary>
    bool HasRole(string role);

    /// <summary>Belirli bir entity + action izni var mı?</summary>
    bool HasPermission(string entity, string action);

    /// <summary>Scope bazlı izin kontrolü.</summary>
    bool HasPermission(string entity, string action, PermScope scope);
}
```

### 10.3 Permission ve PermScope (CrudKit.Core'da tanımlı)

```csharp
public class Permission
{
    public string Entity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public PermScope Scope { get; set; }
}

public enum PermScope
{
    Own,           // Sadece kendi kayıtları
    Department,    // Departmanındaki kayıtlar
    All            // Tüm kayıtlar
}
```

### 10.4 Kullanıcı Tarafı Implementasyon Örnekleri

```csharp
// ---- Örnek 1: JWT tabanlı ----
public class JwtCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public JwtCurrentUser(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public string? Id => User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public string? Username => User?.FindFirst(ClaimTypes.Name)?.Value;
    public string? TenantId => User?.FindFirst("tenant_id")?.Value;
    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
        ?? new List<string>();

    public IReadOnlyList<Permission> Permissions =>
        User?.FindAll("permission").Select(c => ParsePermission(c.Value)).ToList()
        ?? new List<Permission>();

    public bool HasRole(string role) => Roles.Contains(role);
    public bool HasPermission(string entity, string action) =>
        Permissions.Any(p => (p.Entity == entity || p.Entity == "*") && p.Action == action);
    public bool HasPermission(string entity, string action, PermScope scope) =>
        Permissions.Any(p => (p.Entity == entity || p.Entity == "*") && p.Action == action && p.Scope == scope);

    private static Permission ParsePermission(string value)
    {
        // Format: "entity:action:scope" → "invoices:approve:all"
        var parts = value.Split(':');
        return new Permission
        {
            Entity = parts.ElementAtOrDefault(0) ?? "",
            Action = parts.ElementAtOrDefault(1) ?? "",
            Scope = Enum.TryParse<PermScope>(parts.ElementAtOrDefault(2), true, out var s) ? s : PermScope.Own
        };
    }
}

// ---- Örnek 2: Keycloak ----
public class KeycloakCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public string? Id => User?.FindFirst("sub")?.Value;
    public string? Username => User?.FindFirst("preferred_username")?.Value;
    public string? TenantId => User?.FindFirst("tenant_id")?.Value;

    public IReadOnlyList<string> Roles
    {
        get
        {
            // Keycloak rolleri realm_access.roles claim'inde JSON array olarak gelir
            var realmAccess = User?.FindFirst("realm_access")?.Value;
            if (realmAccess == null) return new List<string>();
            var json = JsonSerializer.Deserialize<JsonElement>(realmAccess);
            return json.GetProperty("roles").EnumerateArray()
                .Select(r => r.GetString()!)
                .ToList();
        }
    }

    // ... diğer implementasyonlar
}

// ---- Örnek 3: Azure AD ----
public class AzureAdCurrentUser : ICurrentUser
{
    public string? Id => User?.FindFirst("oid")?.Value;
    public string? Username => User?.FindFirst("upn")?.Value;
    public string? TenantId => User?.FindFirst("tid")?.Value;
    public IReadOnlyList<string> Roles =>
        User?.FindAll("roles").Select(c => c.Value).ToList() ?? new List<string>();
    // ...
}

// ---- Örnek 4: Test / geliştirme ----
public class FakeCurrentUser : ICurrentUser
{
    public string? Id { get; set; } = "dev-user-1";
    public string? Username { get; set; } = "developer";
    public string? TenantId { get; set; } = "dev-tenant";
    public IReadOnlyList<string> Roles { get; set; } = new List<string> { "admin" };
    public IReadOnlyList<Permission> Permissions { get; set; } = new List<Permission>();
    public bool IsAuthenticated => true;
    public bool HasRole(string role) => Roles.Contains(role);
    public bool HasPermission(string entity, string action) => true;  // dev modda her şeye izin
    public bool HasPermission(string entity, string action, PermScope scope) => true;
}

// ---- Örnek 5: Anonymous (auth zorunlu olmayan endpoint'ler) ----
// ICurrentUser DI'da kayıtlı değilse veya token yoksa
// CrudKit varsayılan AnonymousCurrentUser kullanır
public class AnonymousCurrentUser : ICurrentUser
{
    public string? Id => null;
    public string? Username => null;
    public string? TenantId => null;
    public IReadOnlyList<string> Roles => new List<string>();
    public IReadOnlyList<Permission> Permissions => new List<Permission>();
    public bool IsAuthenticated => false;
    public bool HasRole(string role) => false;
    public bool HasPermission(string entity, string action) => false;
    public bool HasPermission(string entity, string action, PermScope scope) => false;
}
```

### 10.5 CrudKit İçinde Kullanım

```csharp
// ---- AppContext güncellendi ----
public class AppContext
{
    public required IServiceProvider Services { get; init; }
    public required ICurrentUser CurrentUser { get; init; }
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    // Kısayollar
    public string? TenantId => CurrentUser.TenantId;
    public string? UserId => CurrentUser.Id;
    public bool IsAuthenticated => CurrentUser.IsAuthenticated;
}

// ---- EfRepo<T> — ICurrentUser kullanır ----
public class EfRepo<T> : IRepo<T> where T : class, IEntity
{
    private readonly DbContext _db;
    private readonly ICurrentUser _currentUser;

    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();

        // Multi-tenant filtresi — tenant_id ICurrentUser'dan gelir
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(T)) && _currentUser.TenantId != null)
            query = query.Where(e => ((IMultiTenant)e).TenantId == _currentUser.TenantId);

        // Soft delete filtresi
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
            query = query.Where(e => ((ISoftDeletable)e).DeletedAt == null);

        return await _queryBuilder.Apply(query, listParams, ct);
    }
}

// ---- CrudKitDbContext — ICurrentUser kullanır ----
// Audit log: kim değiştirdi? → _currentUser.Id
// Tenant filtresi: hangi tenant? → _currentUser.TenantId
// Timestamp, soft delete, Id üretimi → otomatik

// ---- Hook'lar — AppContext üzerinden ICurrentUser'a erişir ----
public class OrderHooks : ICrudHooks<Order>
{
    public Task BeforeCreate(Order entity, AppContext ctx)
    {
        // Siparişi oluşturan kişiyi kaydet
        entity.CreatedBy = ctx.CurrentUser.Id;

        // Sadece admin toptan indirim yapabilir
        if (entity.Discount > 20 && !ctx.CurrentUser.HasRole("admin"))
            throw AppError.Forbidden("20% üstü indirim için admin yetkisi gerekli");

        return Task.CompletedTask;
    }
}
```

### 10.6 Endpoint Koruma Filtreleri

```csharp
// CrudKit yine de endpoint seviyesinde yetki kontrolü sunar.
// Ama bu kontrol ICurrentUser üzerinden yapılır, token doğrulama değil.

// ---- RequireAuthFilter ----
public class RequireAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.IsAuthenticated)
            return Results.Problem(statusCode: 401, title: "Unauthorized");
        return await next(ctx);
    }
}

// ---- RequireRoleFilter ----
public class RequireRoleFilter : IEndpointFilter
{
    private readonly string _role;
    public RequireRoleFilter(string role) => _role = role;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.HasRole(_role))
            return Results.Problem(statusCode: 403, title: "Forbidden");
        return await next(ctx);
    }
}

// ---- RequirePermissionFilter ----
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
        var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (!currentUser.HasPermission(_entity, _action))
            return Results.Problem(statusCode: 403, title: "Forbidden");
        return await next(ctx);
    }
}

// ---- Kullanım ----
app.MapCrudEndpoints<User, CreateUser, UpdateUser>("users")
    .RequireAuth();

app.MapCrudEndpoints<Invoice, CreateInvoice, UpdateInvoice>("invoices")
    .RequireAuth()
    .RequireRole("finance");

// Endpoint bazlı özel izin
app.MapPost("/api/invoices/{id}/approve", ApproveHandler)
    .RequirePermission("invoices", "approve");

// Extension method'lar
public static RouteGroupBuilder RequireAuth(this RouteGroupBuilder group)
{
    group.AddEndpointFilter<RequireAuthFilter>();
    return group;
}

public static RouteGroupBuilder RequireRole(this RouteGroupBuilder group, string role)
{
    group.AddEndpointFilter(new RequireRoleFilter(role));
    return group;
}

public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string entity, string action)
{
    builder.AddEndpointFilter(new RequirePermissionFilter(entity, action));
    return builder;
}
```

### 10.7 DI Kayıt

```csharp
// ---- Program.cs — kullanıcı tarafı ----

// JWT kullanıyorsan
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => { /* kendi JWT config'in */ });
builder.Services.AddScoped<ICurrentUser, JwtCurrentUser>();

// Keycloak kullanıyorsan
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.Authority = "https://keycloak.example.com/realms/myrealm";
    options.Audience = "my-api";
});
builder.Services.AddScoped<ICurrentUser, KeycloakCurrentUser>();

// Azure AD kullanıyorsan
builder.Services.AddAuthentication().AddMicrosoftIdentityWebApi(builder.Configuration);
builder.Services.AddScoped<ICurrentUser, AzureAdCurrentUser>();

// Geliştirme ortamı
if (builder.Environment.IsDevelopment())
    builder.Services.AddScoped<ICurrentUser, FakeCurrentUser>();

// Kullanıcının kendi DbContext'i
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// CrudKit — auth'tan ve DB provider'dan bağımsız
builder.Services.AddCrudKit<AppDbContext>();
```

### 10.8 ICurrentUser Testleri

```csharp
// ---- CurrentUserTests.cs (CrudKit.Core.Tests) ----
public class CurrentUserTests
{
    [Fact]
    public void AnonymousUser_ShouldNotBeAuthenticated()
    {
        var user = new AnonymousCurrentUser();
        Assert.False(user.IsAuthenticated);
        Assert.Null(user.Id);
        Assert.Null(user.TenantId);
        Assert.False(user.HasRole("admin"));
        Assert.False(user.HasPermission("invoices", "read"));
    }

    [Fact]
    public void FakeUser_ShouldBeAuthenticated()
    {
        var user = new FakeCurrentUser
        {
            Id = "test-1",
            Username = "testuser",
            TenantId = "tenant-1",
            Roles = new List<string> { "admin", "finance" },
        };

        Assert.True(user.IsAuthenticated);
        Assert.Equal("test-1", user.Id);
        Assert.Equal("tenant-1", user.TenantId);
        Assert.True(user.HasRole("admin"));
        Assert.True(user.HasRole("finance"));
        Assert.False(user.HasRole("superadmin"));
    }

    [Fact]
    public void HasPermission_ShouldCheckEntityAndAction()
    {
        var user = new FakeCurrentUser
        {
            Id = "test-1",
            Permissions = new List<Permission>
            {
                new() { Entity = "invoices", Action = "read", Scope = PermScope.All },
                new() { Entity = "invoices", Action = "approve", Scope = PermScope.Department },
                new() { Entity = "orders", Action = "create", Scope = PermScope.Own },
            }
        };

        // Entity + action
        Assert.True(user.HasPermission("invoices", "read"));
        Assert.True(user.HasPermission("invoices", "approve"));
        Assert.True(user.HasPermission("orders", "create"));
        Assert.False(user.HasPermission("invoices", "delete"));
        Assert.False(user.HasPermission("users", "read"));
    }

    [Fact]
    public void HasPermission_ShouldCheckScope()
    {
        var user = new FakeCurrentUser
        {
            Id = "test-1",
            Permissions = new List<Permission>
            {
                new() { Entity = "invoices", Action = "read", Scope = PermScope.Own },
            }
        };

        Assert.True(user.HasPermission("invoices", "read", PermScope.Own));
        Assert.False(user.HasPermission("invoices", "read", PermScope.All));
        Assert.False(user.HasPermission("invoices", "read", PermScope.Department));
    }

    [Fact]
    public void WildcardEntity_ShouldMatchAll()
    {
        var user = new FakeCurrentUser
        {
            Id = "test-1",
            Permissions = new List<Permission>
            {
                new() { Entity = "*", Action = "read", Scope = PermScope.All },
            }
        };

        Assert.True(user.HasPermission("invoices", "read"));
        Assert.True(user.HasPermission("orders", "read"));
        Assert.True(user.HasPermission("users", "read"));
        Assert.False(user.HasPermission("users", "delete"));
    }
}

// ---- RequireAuthFilterTests.cs (CrudKit.Api.Tests) ----
public class RequireAuthFilterTests
{
    [Fact]
    public async Task ShouldReturn401WhenNotAuthenticated()
    {
        // AnonymousCurrentUser ile endpoint'e istek at
        // 401 dönmeli
    }

    [Fact]
    public async Task ShouldAllowWhenAuthenticated()
    {
        // FakeCurrentUser (IsAuthenticated = true) ile istek at
        // 200 dönmeli
    }
}

// ---- RequireRoleFilterTests.cs (CrudKit.Api.Tests) ----
public class RequireRoleFilterTests
{
    [Fact]
    public async Task ShouldReturn403WhenRoleMissing()
    {
        // FakeCurrentUser(Roles = ["user"]) ile "admin" gerektiren endpoint'e istek at
        // 403 dönmeli
    }

    [Fact]
    public async Task ShouldAllowWhenRoleExists()
    {
        // FakeCurrentUser(Roles = ["admin"]) ile "admin" gerektiren endpoint'e istek at
        // 200 dönmeli
    }
}

// ---- EfRepo tenant filtresi ICurrentUser ile çalışıyor mu testi ----
// (CrudKit.EntityFrameworkCore.Tests)
public class MultiTenantRepoTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task List_ShouldFilterByCurrentUserTenant()
    {
        // Tenant-1 ve Tenant-2 için kayıtlar oluştur
        // ICurrentUser.TenantId = "tenant-1" olarak set et
        // List çağır
        // Sadece tenant-1 kayıtları dönmeli
    }

    [Fact]
    public async Task FindById_ShouldRejectCrossTenantAccess()
    {
        // Tenant-2'ye ait bir kaydı Tenant-1 kullanıcısıyla getirmeye çalış
        // NotFound veya Forbidden dönmeli
    }
}
```

---

