## 1. CrudKit.Core

Hiçbir framework bağımlılığı olmayan saf interface, attribute ve model katmanı.

### 1.1 Dosya Yapısı

```
CrudKit.Core/
├── Interfaces/
│   ├── IEntity.cs
│   ├── ICurrentUser.cs
│   ├── ICrudHooks.cs
│   ├── ISoftDeletable.cs
│   ├── ICascadeSoftDelete.cs
│   ├── IAuditable.cs
│   ├── IMultiTenant.cs
│   ├── IStateMachine.cs
│   ├── IDocumentNumbering.cs
│   ├── IEventBus.cs
│   ├── IEntityMapper.cs
│   └── IModule.cs
├── Attributes/
│   ├── CrudEntityAttribute.cs
│   ├── SearchableAttribute.cs
│   ├── UniqueAttribute.cs
│   ├── ProtectedAttribute.cs
│   ├── SkipResponseAttribute.cs
│   ├── SkipUpdateAttribute.cs
│   ├── HashedAttribute.cs
│   ├── CascadeSoftDeleteAttribute.cs
│   └── DefaultIncludeAttribute.cs
├── Models/
│   ├── Paginated.cs
│   ├── Optional.cs
│   ├── AppError.cs
│   ├── ValidationErrors.cs
│   ├── FieldError.cs
│   ├── FilterOp.cs
│   ├── ListParams.cs
│   └── SortDirection.cs
├── Serialization/
│   ├── OptionalJsonConverterFactory.cs
│   └── OptionalJsonConverter.cs
├── Context/
│   └── AppContext.cs
├── Events/
│   ├── IEvent.cs
│   ├── EntityCreatedEvent.cs
│   ├── EntityUpdatedEvent.cs
│   └── EntityDeletedEvent.cs
└── Enums/
    └── PermScope.cs
```

### 1.2 Interface Tanımları

```csharp
// ---- IEntity ----
public interface IEntity
{
    string Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

// ---- ICrudHooks<T> ----
// Tüm metodların default implementasyonu boş olmalı.
// Kullanıcı sadece ihtiyacı olan hook'u override eder.
public interface ICrudHooks<T> where T : class, IEntity
{
    Task BeforeCreate(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterCreate(T entity, AppContext ctx) => Task.CompletedTask;
    Task BeforeUpdate(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterUpdate(T entity, AppContext ctx) => Task.CompletedTask;
    Task BeforeDelete(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(T entity, AppContext ctx) => Task.CompletedTask;
    Task BeforeRestore(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterRestore(T entity, AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// List ve FindById sorgularına ek filtre uygular.
    /// Row-level security, Own scope gibi yetkilendirme filtreleri buraya yazılır.
    /// Default: query olduğu gibi döner.
    /// </summary>
    IQueryable<T> ApplyScope(IQueryable<T> query, AppContext ctx) => query;
}

// ---- ISoftDeletable ----
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}

// ---- IAuditable ----
public interface IAuditable
{
    // Marker interface — EfRepo otomatik audit log yazar
}

// ---- IMultiTenant ----
public interface IMultiTenant
{
    string TenantId { get; set; }
}

// ---- IStateMachine<TState> ----
public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}

// ---- IDocumentNumbering ----
public interface IDocumentNumbering
{
    string DocumentNumber { get; set; }
    static abstract string Prefix { get; }
    static abstract bool YearlyReset { get; }
}

// ---- IEventBus ----
public interface IEventBus
{
    Task Publish<T>(T @event, CancellationToken ct = default) where T : class, IEvent;
    void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent;
}

// ---- IEvent ----
public interface IEvent
{
    string EventId { get; }
    DateTime OccurredAt { get; }
}

// ---- IModule ----
// Modular monolith desteği için.
// Kullanıcı kendi modüllerini bu interface ile tanımlar.
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
    void RegisterWorkflowActions(ActionRegistry registry) { }
}
```

### 1.3 Attribute Tanımları

```csharp
// ---- CrudEntityAttribute ----
[AttributeUsage(AttributeTargets.Class)]
public class CrudEntityAttribute : Attribute
{
    public string Table { get; set; } = string.Empty;
    public bool SoftDelete { get; set; }
    public bool Audit { get; set; }
    public bool MultiTenant { get; set; }
    public string? Workflow { get; set; }
    public string[]? WorkflowProtected { get; set; }
    public string? NumberingPrefix { get; set; }
    public bool NumberingYearlyReset { get; set; } = true;
}

// ---- Field-level attribute'lar ----
[AttributeUsage(AttributeTargets.Property)]
public class SearchableAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class UniqueAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class ProtectedAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class SkipResponseAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class SkipUpdateAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class HashedAttribute : Attribute { }
```

Not: `[Required]`, `[MaxLength]`, `[MinLength]`, `[Range]`, `[EmailAddress]`, `[RegularExpression]` gibi validasyon attribute'ları System.ComponentModel.DataAnnotations'tan kullanılır. CrudKit bunları tekrar tanımlamaz.

### 1.4 Model Tanımları

```csharp
// ---- Paginated<T> ----
public class Paginated<T>
{
    public List<T> Data { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalPages { get; set; }
}

// ---- FilterOp ----
public class FilterOp
{
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;
    public List<string>? Values { get; set; }  // "in" operatörü için

    // Desteklenen operatörler:
    // eq, neq, gt, gte, lt, lte, like, starts, in, null, notnull
    //
    // Kullanım: ?field=gte:18  ?field=like:ali  ?field=in:a,b,c  ?field=null
    // Operatör belirtilmezse eq varsayılır: ?field=value → eq:value

    public static FilterOp Parse(string raw);
}

// ---- ListParams ----
public class ListParams
{
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;  // max 100
    public string? Sort { get; set; }        // "-created_at,username" — prefix ile DESC
    public Dictionary<string, FilterOp> Filters { get; set; } = new();

    public static ListParams FromQuery(IQueryCollection query);
}

// ---- AppContext ----
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

// ---- AppError ----
public class AppError : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public object? Details { get; }

    // Factory metodlar
    public static AppError NotFound(string message = "Kayıt bulunamadı");
    public static AppError BadRequest(string message);
    public static AppError Unauthorized(string message = "Yetkisiz erişim");
    public static AppError Forbidden(string message = "Erişim engellendi");
    public static AppError Validation(ValidationErrors errors);
    public static AppError Conflict(string message);
}

// ---- ValidationErrors ----
public class ValidationErrors
{
    public List<FieldError> Errors { get; set; } = new();

    public void Add(string field, string code, string message);
    public bool IsEmpty => Errors.Count == 0;
    public void ThrowIfInvalid();
}

public record FieldError(string Field, string Code, string Message);
```

---

