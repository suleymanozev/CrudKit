## 2. CrudKit.EntityFrameworkCore

EF Core üzerine kurulu veri erişim katmanı. Generic repository, query builder, tenant filter, audit log, soft delete, document numbering.

### 2.1 Dosya Yapısı

```
CrudKit.EntityFrameworkCore/
├── CrudKitDbContext.cs                    # Abstract base — tüm cross-cutting concern'ler
├── Repository/
│   ├── IRepo.cs
│   └── EfRepo.cs
├── Query/
│   ├── QueryBuilder.cs
│   ├── FilterApplier.cs
│   ├── SortApplier.cs
│   ├── IncludeApplier.cs
│   └── IQueryableExtensions.cs
├── Dialect/
│   ├── IDbDialect.cs
│   ├── PostgresDialect.cs
│   ├── SqlServerDialect.cs
│   ├── SqliteDialect.cs
│   ├── GenericDialect.cs
│   └── DialectDetector.cs
├── Models/
│   ├── AuditLogEntry.cs
│   ├── SequenceEntry.cs
│   └── IdempotencyRecord.cs
├── Numbering/
│   └── SequenceGenerator.cs
├── Concurrency/
│   └── IConcurrent.cs
├── Configuration/
│   └── CrudKitDbOptions.cs
└── Extensions/
    ├── ServiceCollectionExtensions.cs
    └── ModelBuilderExtensions.cs
```

### 2.2 CrudKitDbContext — Abstract Base

Tüm cross-cutting concern'leri tek yerde toplar. Kullanıcı bu sınıftan türetir, sadece `DbSet<>` tanımlar. Soft delete filtresi, tenant filtresi, timestamp yönetimi, Id üretimi, unique index, enum conversion, concurrency token, audit log tablosu — hepsi otomatik.

```csharp
public abstract class CrudKitDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;

    // CrudKit'in kendi tabloları
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<SequenceEntry> Sequences => Set<SequenceEntry>();

    protected CrudKitDbContext(
        DbContextOptions options,
        ICurrentUser currentUser) : base(options)
    {
        _currentUser = currentUser;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // ---- Soft delete global filter ----
            if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType).HasQueryFilter(
                    BuildSoftDeleteFilter(clrType));
            }

            // ---- Multi-tenant global filter ----
            // Soft delete + tenant filtresi varsa ikisini birleştirir
            if (typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                var tenantFilter = BuildTenantFilter(clrType);
                var softFilter = typeof(ISoftDeletable).IsAssignableFrom(clrType)
                    ? BuildSoftDeleteFilter(clrType) : null;

                modelBuilder.Entity(clrType).HasQueryFilter(
                    CombineFilters(tenantFilter, softFilter));
            }

            // ---- Concurrency token ----
            if (typeof(IConcurrent).IsAssignableFrom(clrType))
            {
                modelBuilder.Entity(clrType)
                    .Property(nameof(IConcurrent.RowVersion))
                    .IsRowVersion();
            }

            // ---- Enum'ları string olarak sakla ----
            foreach (var property in clrType.GetProperties())
            {
                if (property.PropertyType.IsEnum)
                {
                    modelBuilder.Entity(clrType)
                        .Property(property.Name)
                        .HasConversion<string>();
                }
            }

            // ---- [Unique] attribute → unique index ----
            // Soft delete entity'lerde WHERE deleted_at IS NULL partial index
            foreach (var prop in clrType.GetProperties())
            {
                if (prop.GetCustomAttribute<UniqueAttribute>() != null)
                {
                    var indexBuilder = modelBuilder.Entity(clrType)
                        .HasIndex(prop.Name)
                        .IsUnique();

                    if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
                        indexBuilder.HasFilter("deleted_at IS NULL");
                }
            }
        }

        // CrudKit kendi tabloları
        modelBuilder.Entity<AuditLogEntry>(b =>
        {
            b.ToTable("__crud_audit_logs");
            b.HasIndex(e => new { e.EntityType, e.EntityId });
            b.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<SequenceEntry>(b =>
        {
            b.ToTable("__crud_sequences");
            b.HasIndex(e => new { e.EntityType, e.TenantId, e.Year }).IsUnique();
        });

        // Kullanıcının kendi konfigürasyonu
        OnModelCreatingCustom(modelBuilder);
    }

    /// <summary>
    /// Kullanıcının override edebileceği hook.
    /// Entity configuration, seed data vb. buraya yazılır.
    /// base.OnModelCreating'i çağırmaya gerek yok.
    /// </summary>
    protected virtual void OnModelCreatingCustom(ModelBuilder modelBuilder) { }

    // ---- SaveChanges override — otomatik Id, timestamp, tenant, soft delete ----

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BeforeSaveChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken ct = default)
    {
        BeforeSaveChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void BeforeSaveChanges()
    {
        foreach (var entry in ChangeTracker.Entries<IEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // Id üretimi
                    if (string.IsNullOrEmpty(entry.Entity.Id))
                        entry.Entity.Id = Guid.NewGuid().ToString();
                    // Timestamp
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    // Multi-tenant: tenant_id otomatik set
                    if (entry.Entity is IMultiTenant tenantEntity && _currentUser.TenantId != null)
                        tenantEntity.TenantId = _currentUser.TenantId;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    // CreatedAt değiştirilemez
                    entry.Property(nameof(IEntity.CreatedAt)).IsModified = false;
                    break;

                case EntityState.Deleted:
                    // ISoftDeletable ise gerçekten silme, soft delete yap
                    if (entry.Entity is ISoftDeletable softDelete)
                    {
                        entry.State = EntityState.Modified;
                        softDelete.DeletedAt = DateTime.UtcNow;
                    }
                    break;
            }
        }

        // Audit log — IAuditable entity'ler için
        WriteAuditLogs();
    }

    private void WriteAuditLogs()
    {
        var auditEntries = new List<AuditLogEntry>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is not IAuditable) continue;
            if (entry.State == EntityState.Detached || entry.State == EntityState.Unchanged) continue;

            var auditEntry = new AuditLogEntry
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = (entry.Entity as IEntity)?.Id ?? "",
                UserId = _currentUser.Id,
                Timestamp = DateTime.UtcNow,
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    auditEntry.Action = "Create";
                    auditEntry.NewValues = SerializeProperties(entry, entry.Properties);
                    break;
                case EntityState.Modified:
                    auditEntry.Action = "Update";
                    var modified = entry.Properties.Where(p => p.IsModified);
                    auditEntry.OldValues = SerializeOriginalValues(entry, modified);
                    auditEntry.NewValues = SerializeProperties(entry, modified);
                    auditEntry.ChangedFields = JsonSerializer.Serialize(
                        modified.Select(p => p.Metadata.Name));
                    break;
                case EntityState.Deleted:
                    auditEntry.Action = "Delete";
                    auditEntry.OldValues = SerializeProperties(entry, entry.Properties);
                    break;
            }

            auditEntries.Add(auditEntry);
        }

        if (auditEntries.Any())
            AuditLogs.AddRange(auditEntries);
    }

    // ---- Tenant filter'da runtime değer ----
    // EF Core global filter'da runtime değer bu property üzerinden gelir
    private string? CurrentTenantId => _currentUser.TenantId;

    // ---- Filter builder helper'lar ----

    private static LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        // e => ((ISoftDeletable)e).DeletedAt == null
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(ISoftDeletable.DeletedAt));
        var condition = Expression.Equal(prop, Expression.Constant(null, typeof(DateTime?)));
        return Expression.Lambda(condition, param);
    }

    private LambdaExpression BuildTenantFilter(Type entityType)
    {
        // e => ((IMultiTenant)e).TenantId == CurrentTenantId
        var param = Expression.Parameter(entityType, "e");
        var prop = Expression.Property(param, nameof(IMultiTenant.TenantId));
        var tenantId = Expression.Property(
            Expression.Constant(this),
            typeof(CrudKitDbContext).GetProperty("CurrentTenantId",
                BindingFlags.NonPublic | BindingFlags.Instance)!);
        var condition = Expression.Equal(prop, tenantId);
        return Expression.Lambda(condition, param);
    }

    private static LambdaExpression CombineFilters(
        LambdaExpression filter1, LambdaExpression? filter2)
    {
        if (filter2 == null) return filter1;
        var param = filter1.Parameters[0];
        var body = Expression.AndAlso(
            filter1.Body,
            Expression.Invoke(filter2, param));
        return Expression.Lambda(body, param);
    }

    private static string SerializeProperties(EntityEntry entry, IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(props.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));

    private static string SerializeOriginalValues(EntityEntry entry, IEnumerable<PropertyEntry> props)
        => JsonSerializer.Serialize(props.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
}
```

```csharp
// ---- IConcurrent — optimistic concurrency ----
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
```

```csharp
// ---- SequenceEntry — belge numaralandırma tablosu ----
public class SequenceEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EntityType { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Year { get; set; } = string.Empty;
    public long CurrentVal { get; set; }
}
```

```csharp
// ---- Kullanıcı tarafı — sadece DbSet ekler ----
public class AppDbContext : CrudKitDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    // ... 80 entity daha

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ICurrentUser currentUser) : base(options, currentUser) { }

    protected override void OnModelCreatingCustom(ModelBuilder modelBuilder)
    {
        // Opsiyonel: entity konfigürasyonlarını ayrı dosyalardan yükle
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

CrudKitDbContext otomatik olarak şunları halleder:
- `IEntity` → Id üretimi (Guid), CreatedAt/UpdatedAt timestamp (UTC)
- `ISoftDeletable` → DELETE'i soft delete'e çevirir, global query filter
- `ISoftDeletable` + `[Unique]` → partial unique index (WHERE deleted_at IS NULL)
- `IMultiTenant` → global tenant filtresi, create'de otomatik tenant_id set
- `IConcurrent` → EF Core RowVersion ile optimistic concurrency
- `IAuditable` → SaveChanges'da otomatik audit log yazımı (eski/yeni değer, değişen alanlar)
- Enum property'ler → string olarak DB'de saklanır
- CreatedAt → update'de değiştirilemez

### 2.3 IRepo<T> Interface

```csharp
public interface IRepo<T> where T : class, IEntity
{
    Task<T> FindById(string id, CancellationToken ct = default);
    Task<T?> FindByIdOrDefault(string id, CancellationToken ct = default);
    Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default);
    Task<List<T>> FindByField(string fieldName, object value, CancellationToken ct = default);
    Task<T> Create(object createDto, CancellationToken ct = default);
    Task<T> Update(string id, object updateDto, CancellationToken ct = default);
    Task Delete(string id, CancellationToken ct = default);
    Task<bool> Exists(string id, CancellationToken ct = default);
    Task<long> Count(CancellationToken ct = default);

    // Soft delete entity'ler için
    Task Restore(string id, CancellationToken ct = default);
}
```

### 2.4 EfRepo<T> Implementasyonu

EfRepo `CrudKitDbContext`'e bağımlıdır. Soft delete, tenant filtresi, timestamp, audit gibi cross-cutting concern'ler DbContext'te halledildiği için Repo sadece iş mantığına odaklanır.

```csharp
public class EfRepo<TContext, T> : IRepo<T>
    where TContext : CrudKitDbContext    // DbContext değil, CrudKitDbContext
    where T : class, IEntity
{
    private readonly TContext _db;
    private readonly QueryBuilder<T> _queryBuilder;
    private readonly ICurrentUser _currentUser;

    public EfRepo(TContext db, QueryBuilder<T> queryBuilder, ICurrentUser currentUser)
    {
        _db = db;
        _queryBuilder = queryBuilder;
        _currentUser = currentUser;
    }

    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();
        // Soft delete ve tenant filtreleri CrudKitDbContext.HasQueryFilter'da
        // otomatik uygulanır — burada tekrar yazmaya gerek yok
        return await _queryBuilder.Apply(query, listParams, ct);
    }

    public async Task<T> Create(object createDto, CancellationToken ct = default)
    {
        // 1. DTO property'lerini entity'ye map et (reflection ile)
        // 2. Id = Guid.NewGuid().ToString()
        // 3. CreatedAt = UpdatedAt = DateTime.UtcNow
        // 4. IMultiTenant ise TenantId = _currentUser.TenantId
        // 5. IDocumentNumbering ise DocumentNumber = await SequenceGenerator.Next()
        // 6. [Hashed] alanları BCrypt ile hash'le
        // 7. SaveChanges
        // 8. [SkipResponse] alanları null'la ve döndür
    }

    public async Task<T> Update(string id, object updateDto, CancellationToken ct = default)
    {
        // 1. Entity'yi getir (yoksa NotFound)
        // 2. [Protected] ve [SkipUpdate] alanları atla
        // 3. Workflow korumalı alanları atla
        // 4. Sadece DTO'da null olmayan property'leri güncelle (partial update)
        // 5. UpdatedAt = DateTime.UtcNow
        // 6. SaveChanges
        // 7. [SkipResponse] alanları null'la ve döndür
    }

    public async Task Delete(string id, CancellationToken ct = default)
    {
        // ISoftDeletable ise: DeletedAt = DateTime.UtcNow
        // Değilse: Remove
    }

    public async Task Restore(string id, CancellationToken ct = default)
    {
        // Sadece ISoftDeletable entity'ler için
        // DeletedAt = null
    }
}
```

### 2.5 QueryBuilder<T>

```csharp
public class QueryBuilder<T> where T : class, IEntity
{
    public async Task<Paginated<T>> Apply(
        IQueryable<T> query,
        ListParams listParams,
        CancellationToken ct = default)
    {
        // 1. Filtreleri uygula — FilterApplier
        // 2. Toplam sayıyı al (filtreler uygulandıktan sonra)
        // 3. Sıralama uygula — SortApplier
        // 4. Sayfalama uygula — Skip/Take
        // 5. Paginated<T> döndür
    }
}
```

### 2.6 FilterApplier

```csharp
public class FilterApplier
{
    private readonly IDbDialect _dialect;

    public FilterApplier(IDbDialect dialect) => _dialect = dialect;

    // Her FilterOp için Expression<Func<T, bool>> üretir.
    // Sadece entity üzerinde gerçekten var olan property'lere filtre uygular (SQL injection koruması).
    //
    // Standart operatörler (dialect gerekmez):
    //   eq       → property == value
    //   neq      → property != value
    //   gt       → property > value
    //   gte      → property >= value
    //   lt       → property < value
    //   lte      → property <= value
    //   in       → values.Contains(property)
    //   null     → property == null
    //   notnull  → property != null
    //
    // Dialect'e delege edilen operatörler:
    //   like     → _dialect.ApplyLike()     (PostgreSQL: ILike, SQL Server: LIKE, SQLite: LOWER+LIKE)
    //   starts   → _dialect.ApplyStartsWith()
    //
    // Tip dönüşümü: property tipine göre value'yu Convert eder.
    //   string  → olduğu gibi
    //   int     → int.Parse
    //   long    → long.Parse
    //   decimal → decimal.Parse
    //   double  → double.Parse
    //   bool    → bool.Parse veya "1"/"0"
    //   DateTime → DateTime.Parse
    //   Enum    → Enum.Parse

    public IQueryable<T> Apply<T>(
        IQueryable<T> query,
        string propertyName,
        FilterOp op) where T : class
    {
        return op.Operator switch
        {
            "like"   => _dialect.ApplyLike(query, BuildPropertyExpression<T>(propertyName), op.Value),
            "starts" => _dialect.ApplyStartsWith(query, BuildPropertyExpression<T>(propertyName), op.Value),
            _        => ApplyStandard(query, propertyName, op)  // eq, neq, gt, gte, lt, lte, in, null, notnull
        };
    }
}
```

### 2.7 SortApplier

```csharp
public static class SortApplier
{
    // Sort string formatı: "-created_at,username"
    // - prefix → DESC, prefix yok → ASC
    // Virgülle birden fazla alan desteklenir.
    // İlk alan OrderBy, sonrakiler ThenBy.
    // Geçersiz alan adları sessizce yoksayılır.
    // Sort belirtilmezse varsayılan: CreatedAt DESC

    public static IQueryable<T> Apply<T>(
        IQueryable<T> query,
        string? sortString) where T : class;
}
```

### 2.8 Database Dialect Sistemi

Provider'lar arası SQL farklılıklarını soyutlar. Operatörlerin %80'i standart LINQ expression ile çalışır, dialect'e ihtiyaç duymaz. Sadece provider'a özgü davranış gerektiren %20'lik kısım `IDbDialect` üzerinden soyutlanır.

```
── Ne standart, ne dialect'e gider ──

Standart (dialect gerekmez)         Dialect'e bağımlı
───────────────────────────         ────────────────────────
eq, neq    → ==, !=                 like   → ILike vs LIKE vs LOWER
gt, gte    → >, >=                  starts → ILike vs LIKE
lt, lte    → <, <=                  Upsert → ON CONFLICT vs MERGE
in         → Contains               Sequence → nextval vs NEXT VALUE FOR
null       → == null                JSON sorgulama → jsonb vs JSON_VALUE
notnull    → != null                Full-text → tsvector vs FREETEXT
sort       → OrderBy/ThenBy         Locking → FOR UPDATE vs WITH (UPDLOCK)
page       → Skip/Take
```

```csharp
// ---- IDbDialect ----
public interface IDbDialect
{
    /// <summary>Case-insensitive LIKE araması. Provider'a göre farklı çalışır.</summary>
    IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class;

    /// <summary>Case-insensitive başlangıç araması.</summary>
    IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class;

    /// <summary>Concurrent-safe upsert SQL'i üretir.</summary>
    string GetUpsertSql(string table, string[] columns, string[] keyColumns);

    /// <summary>Sequence'den sonraki değeri almak için SQL.</summary>
    string GetSequenceNextValueSql(string sequenceName);
}
```

```csharp
// ---- PostgresDialect ----
public class PostgresDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // PostgreSQL ILIKE — native case-insensitive
        var parameter = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var method = typeof(NpgsqlDbFunctionsExtensions)
            .GetMethod("ILike", new[] { typeof(DbFunctions), typeof(string), typeof(string) });
        var call = Expression.Call(method!, Expression.Constant(EF.Functions), memberAccess, pattern);
        var lambda = Expression.Lambda<Func<T, bool>>(call, parameter);
        return query.Where(lambda);
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // PostgreSQL ILIKE 'value%'
        // Aynı mantık, pattern farklı
        // ...
        return query;
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var keyList = string.Join(", ", keyColumns);
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) ON CONFLICT ({keyList}) DO UPDATE SET {updateList}";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => $"SELECT nextval('{sequenceName}')";
}
```

```csharp
// ---- SqlServerDialect ----
public class SqlServerDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // SQL Server varsayılan collation zaten case-insensitive (CI_AS)
        // EF.Functions.Like yeterli
        var parameter = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var method = typeof(DbFunctionsExtensions)
            .GetMethod("Like", new[] { typeof(DbFunctions), typeof(string), typeof(string) });
        var call = Expression.Call(method!, Expression.Constant(EF.Functions), memberAccess, pattern);
        var lambda = Expression.Lambda<Func<T, bool>>(call, parameter);
        return query.Where(lambda);
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // EF.Functions.Like(prop, 'value%')
        return query;
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        // SQL Server MERGE syntax
        var colList = string.Join(", ", columns);
        var updateList = string.Join(", ", columns.Select(c => $"target.{c} = source.{c}"));
        return $@"MERGE INTO {table} AS target
                  USING (VALUES (...)) AS source ({colList})
                  ON {string.Join(" AND ", keyColumns.Select(k => $"target.{k} = source.{k}"))}
                  WHEN MATCHED THEN UPDATE SET {updateList}
                  WHEN NOT MATCHED THEN INSERT ({colList}) VALUES ({colList});";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => $"SELECT NEXT VALUE FOR {sequenceName}";
}
```

```csharp
// ---- SqliteDialect ----
public class SqliteDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // SQLite LIKE varsayılan olarak case-insensitive (ASCII)
        // Unicode desteği için LOWER kullanılır
        var parameter = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(memberAccess, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
        var contains = Expression.Call(toLower, typeof(string).GetMethod("Contains", new[] { typeof(string) })!, Expression.Constant(value.ToLower()));
        var lambda = Expression.Lambda<Func<T, bool>>(contains, parameter);
        return query.Where(lambda);
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var parameter = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(memberAccess, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
        var startsWith = Expression.Call(toLower, typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!, Expression.Constant(value.ToLower()));
        var lambda = Expression.Lambda<Func<T, bool>>(startsWith, parameter);
        return query.Where(lambda);
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        // SQLite ON CONFLICT — PostgreSQL ile aynı syntax
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var keyList = string.Join(", ", keyColumns);
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) ON CONFLICT ({keyList}) DO UPDATE SET {updateList}";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => throw new NotSupportedException("SQLite does not support sequences. Use autoincrement.");
}
```

```csharp
// ---- GenericDialect — bilinmeyen provider için fallback ----
public class GenericDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // En güvenli yol: ToLower().Contains()
        // Her provider'da çalışır, en yavaş ama en uyumlu
        var parameter = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(memberAccess, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
        var contains = Expression.Call(toLower, typeof(string).GetMethod("Contains", new[] { typeof(string) })!, Expression.Constant(value.ToLower()));
        var lambda = Expression.Lambda<Func<T, bool>>(contains, parameter);
        return query.Where(lambda);
    }

    // ... diğer metodlar da en güvenli fallback ile
}
```

```csharp
// ---- DialectDetector — provider'ı DbContext'ten otomatik algılar ----
public static class DialectDetector
{
    public static IDbDialect Detect(DbContext db)
    {
        var provider = db.Database.ProviderName ?? "";
        return provider switch
        {
            _ when provider.Contains("Npgsql")     => new PostgresDialect(),
            _ when provider.Contains("SqlServer")  => new SqlServerDialect(),
            _ when provider.Contains("Sqlite")     => new SqliteDialect(),
            _                                       => new GenericDialect()
        };
    }
}
```

Kullanıcı dialect seçmek zorunda değildir. `AddCrudKitEf<TContext>()` çağrıldığında `DialectDetector` provider'ı DbContext'ten otomatik algılar ve doğru dialect'i DI'a register eder. İstenirse override edilebilir:

```csharp
// Otomatik algılama (varsayılan — çoğu durumda yeterli)
builder.Services.AddCrudKit<AppDbContext>();

// Manuel override (gerekirse)
builder.Services.AddSingleton<IDbDialect, PostgresDialect>();
```

### 2.9 SequenceGenerator

```csharp
public class SequenceGenerator
{
    // Concurrent-safe belge numarası üretir.
    // sequences tablosu: entity_type, tenant_id, year, current_val
    // INSERT ON CONFLICT DO UPDATE ile race condition önlenir.
    // Format: PREFIX-YYYY-NNNNN (örn: FTR-2026-00042)

    public async Task<string> Next<T>(string tenantId) where T : class, IDocumentNumbering;
}
```

### 2.10 ServiceCollection Extensions

```csharp
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// CrudKit EF Core altyapısını kullanıcının CrudKitDbContext'i ile register eder.
    /// Cross-cutting concern'ler CrudKitDbContext'te.
    /// Dialect otomatik algılanır.
    /// </summary>
    public static IServiceCollection AddCrudKitEf<TContext>(
        this IServiceCollection services)
        where TContext : CrudKitDbContext
    {
        // Dialect — DbContext'ten provider'ı otomatik algıla
        services.TryAddScoped<IDbDialect>(sp =>
        {
            var db = sp.GetRequiredService<TContext>();
            return DialectDetector.Detect(db);
        });

        // Generic repo: IRepo<T> → EfRepo<TContext, T>
        services.AddScoped(typeof(IRepo<>), typeof(EfRepo<,>).MakeGenericType(typeof(TContext), Type.MakeGenericMethodParameter(0)));

        // Query altyapısı
        services.AddScoped(typeof(QueryBuilder<>));
        services.AddScoped<FilterApplier>();
        services.AddScoped<SequenceGenerator>();

        return services;
    }
}
```

Not: `CrudKitDbOptions` kaldırıldı. Soft delete, audit, timestamp gibi davranışlar artık entity'nin implemente ettiği interface'lere göre otomatik belirlenir (`ISoftDeletable`, `IAuditable`, `IConcurrent`). Ayrı bir flag'a gerek yok — interface implemente etmezsen özellik aktif olmaz.

---

