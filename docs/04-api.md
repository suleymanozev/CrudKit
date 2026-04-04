## 4. CrudKit.Api

Minimal API endpoint mapping, validation, schema, error handling.

### 4.1 Dosya Yapısı

```
CrudKit.Api/
├── Endpoints/
│   ├── CrudEndpointMapper.cs
│   ├── DetailEndpointMapper.cs
│   └── WorkflowEndpoints.cs
├── Filters/
│   ├── ValidationFilter.cs
│   ├── AppErrorFilter.cs
│   ├── WorkflowProtectionFilter.cs
│   ├── IdempotencyFilter.cs
│   ├── RequireAuthFilter.cs
│   ├── RequireRoleFilter.cs
│   └── RequirePermissionFilter.cs
├── Middleware/
│   └── RequestLoggingMiddleware.cs
├── Configuration/
│   └── CrudKitApiOptions.cs
└── Extensions/
    └── CrudKitAppExtensions.cs
```

### 4.2 CrudEndpointMapper — Ana Endpoint Mapping

```csharp
public static class CrudEndpointMapper
{
    /// <summary>
    /// Bir entity için 5 standart CRUD endpoint'i register eder.
    /// ISoftDeletable ise restore endpoint'i de eklenir.
    /// IStateMachine ise transition endpoint'i de eklenir.
    /// Workflow bağlıysa workflow endpoint'leri de eklenir.
    /// </summary>
    public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
        this WebApplication app,
        string route,
        Action<CrudEndpointOptions>? configure = null)
        where TEntity : class, IEntity
        where TCreate : class
        where TUpdate : class
    {
        var group = app.MapGroup($"/api/{route}");
        var options = new CrudEndpointOptions();
        configure?.Invoke(options);

        // Standart CRUD
        group.MapGet("/", ListHandler<TEntity>);
        group.MapGet("/{id}", GetHandler<TEntity>);
        group.MapPost("/", CreateHandler<TEntity, TCreate>)
             .AddEndpointFilter<ValidationFilter<TCreate>>();
        group.MapPut("/{id}", UpdateHandler<TEntity, TUpdate>)
             .AddEndpointFilter<ValidationFilter<TUpdate>>();
        group.MapDelete("/{id}", DeleteHandler<TEntity>);

        // ISoftDeletable ise
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
            group.MapPost("/{id}/restore", RestoreHandler<TEntity>);

        // IStateMachine ise
        if (typeof(TEntity).GetInterfaces().Any(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachine<>)))
            group.MapPost("/{id}/transition/{action}", TransitionHandler<TEntity>);

        // Workflow bağlıysa
        var crudAttr = typeof(TEntity).GetCustomAttribute<CrudEntityAttribute>();
        if (crudAttr?.Workflow != null)
        {
            group.MapGet("/{id}/workflow", WorkflowStatusHandler<TEntity>);
            group.MapPost("/{id}/workflow/approve/{stepId}", WorkflowApproveHandler<TEntity>);
            group.MapPost("/{id}/workflow/reject/{stepId}", WorkflowRejectHandler<TEntity>);
            group.MapGet("/{id}/workflow/history", WorkflowHistoryHandler<TEntity>);
            group.MapPost("/{id}/workflow/cancel", WorkflowCancelHandler<TEntity>);
        }

        return group;
    }
}
```

### 4.3 Handler İmplementasyonları

```csharp
// ---- List Handler ----
static async Task<IResult> ListHandler<TEntity>(
    HttpContext ctx,
    IRepo<TEntity> repo,
    CancellationToken ct) where TEntity : class, IEntity
{
    var listParams = ListParams.FromQuery(ctx.Request.Query);
    var result = await repo.List(listParams, ct);
    return Results.Ok(result);
}

// ---- Get Handler ----
static async Task<IResult> GetHandler<TEntity>(
    string id,
    IRepo<TEntity> repo,
    CancellationToken ct) where TEntity : class, IEntity
{
    var entity = await repo.FindByIdOrDefault(id, ct);
    return entity is null ? Results.NotFound() : Results.Ok(entity);
}

// ---- Create Handler ----
// Hook sırası: Validate → BeforeCreate → Insert → AfterCreate → Return
static async Task<IResult> CreateHandler<TEntity, TCreate>(
    TCreate body,
    IRepo<TEntity> repo,
    ICrudHooks<TEntity>? hooks,
    AppContext appCtx,
    CancellationToken ct)
    where TEntity : class, IEntity
    where TCreate : class
{
    var entity = await repo.Create(body, ct);
    // Hook'lar EfRepo içinde veya handler'da çağrılabilir.
    // Tercih: EfRepo.Create içinde çağır, böylece tek sorumluluk kalır.
    return Results.Created($"/api/???/{entity.Id}", entity);
}

// ---- Update Handler ----
// Hook sırası: Validate → BeforeUpdate → Update → AfterUpdate → Return
// Workflow korumalı alanlar: WorkflowProtectionFilter kontrol eder.

// ---- Delete Handler ----
// Hook sırası: BeforeDelete → Delete (veya SoftDelete) → AfterDelete → Return
// Aktif workflow varsa silmeyi engelle.

// ---- Restore Handler ----
// Sadece ISoftDeletable entity'ler için. DeletedAt = null yapar.

// ---- Transition Handler ----
// IStateMachine.Transitions listesinden geçerli geçiş kontrolü.
// Geçersizse BadRequest döner.
```

### 4.4 DetailEndpointMapper

```csharp
public static class DetailEndpointMapper
{
    /// <summary>
    /// Master-detail ilişkisi için nested endpoint'ler register eder.
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
        var group = app.MapGroup($"/api/{masterRoute}/{{masterId}}/{detailRoute}");

        group.MapGet("/", ListByMasterHandler<TMaster, TDetail>);
        group.MapPost("/", CreateForMasterHandler<TMaster, TDetail, TCreateDetail>);
        group.MapGet("/{id}", GetForMasterHandler<TMaster, TDetail>);
        group.MapPut("/batch", BatchUpsertHandler<TMaster, TDetail, TCreateDetail>);
        group.MapDelete("/{id}", DeleteDetailHandler<TDetail>);

        return group;
    }
}

// Batch upsert: Transaction içinde mevcut detail'leri sil, yenilerini ekle.
```

### 4.5 ValidationFilter

```csharp
// DataAnnotation'ları otomatik kontrol eder.
// [Required], [MaxLength], [Range], [EmailAddress], [RegularExpression] vb.
// Hata varsa 400 + ValidationErrors döner.
// Validator kütüphanesi olarak FluentValidation da opsiyonel desteklenir:
//   Eğer DI'da IValidator<TCreate> kayıtlıysa onu çalıştırır,
//   yoksa DataAnnotation'ları kullanır.
```

### 4.6 WorkflowProtectionFilter

```csharp
// [CrudEntity(WorkflowProtected = ["status", "approved_by"])]
// veya [Protected] attribute'u olan alanların
// PUT request'te gönderilmesini engeller.
// Gönderilmişse 400 döner:
// { "error": "'status' alanı workflow tarafından yönetilir" }
```

### 4.7 AppErrorFilter

```csharp
// Handler'lardan fırlatılan AppError exception'larını yakalar.
// Uygun HTTP status code ile ProblemDetails formatında döner.
// NotFound → 404, BadRequest → 400, Unauthorized → 401,
// Forbidden → 403, Validation → 400 + errors, Conflict → 409
```

### 4.8 SkipResponseContractResolver

```csharp
// [SkipResponse] attribute'u olan property'leri JSON serialization'dan çıkarır.
// Örnek: password_hash alanı response'da görünmez.
// System.Text.Json JsonSerializerOptions ile konfigüre edilir.
```

### 4.9 CrudKitAppExtensions

```csharp
public static class CrudKitAppExtensions
{
    /// <summary>
    /// CrudKit altyapısını kullanıcının DbContext'i ile register eder.
    /// Kullanıcı kendi DbContext'ini önceden AddDbContext ile register etmiş olmalıdır.
    /// </summary>
    public static IServiceCollection AddCrudKit<TContext>(
        this IServiceCollection services,
        Action<CrudKitApiOptions>? configure = null)
        where TContext : CrudKitDbContext
    {
        services.AddCrudKitEf<TContext>();

        var apiOptions = new CrudKitApiOptions();
        configure?.Invoke(apiOptions);
        services.AddCrudKitApi(apiOptions);

        // ICurrentUser kayıtlı değilse anonymous fallback
        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        // Otomatik module scan
        if (apiOptions.ScanModulesFromAssembly != null)
        {
            var moduleTypes = apiOptions.ScanModulesFromAssembly
                .GetTypes()
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var moduleType in moduleTypes)
            {
                var module = (IModule)Activator.CreateInstance(moduleType)!;
                module.RegisterServices(services, apiOptions.Configuration!);
                services.AddSingleton(typeof(IModule), moduleType);
            }
        }

        return services;
    }

    /// <summary>
    /// Tek bir modülü manuel olarak register eder.
    /// Assembly scan ile birlikte kullanılabilir.
    /// </summary>
    public static IServiceCollection AddCrudKitModule<TModule>(
        this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        var module = new TModule();
        // RegisterServices burada çağrılamaz — IConfiguration yok
        // Çözüm: IModule instance'ı DI'a eklenir, UseCrudKit'te çağrılır
        services.AddSingleton<IModule>(module);
        return services;
    }

    public static WebApplication UseCrudKit(this WebApplication app)
    {
        app.UseMiddleware<AppErrorFilter>();

        // Tüm module'lerin endpoint'lerini map et
        foreach (var module in app.Services.GetServices<IModule>())
            module.MapEndpoints(app);

        return app;
    }
}

public class CrudKitApiOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public bool EnableSwagger { get; set; } = true;
    public string ApiPrefix { get; set; } = "/api";
    public int BulkLimit { get; set; } = 10_000;

    /// <summary>
    /// Bu assembly'deki tüm IModule implementasyonları otomatik register edilir.
    /// </summary>
    public Assembly? ScanModulesFromAssembly { get; set; }

    /// <summary>
    /// Module'lerin RegisterServices'te kullanacağı IConfiguration.
    /// AddCrudKit tarafından otomatik set edilir.
    /// </summary>
    internal IConfiguration? Configuration { get; set; }
}
```

---

