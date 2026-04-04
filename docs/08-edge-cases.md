## 11. Edge Cases

### 11.1 Partial Update — null vs missing (Optional\<T\>)

Sorun: `UpdateProduct { Name = null, Price = 150 }` geldiğinde `Name = null` iki anlama gelebilir: "Name'e dokunma" (JSON'da yoktu) veya "Name'i null yap" (JSON'da açıkça null gönderildi). Standart C# deserialization ikisini ayırt edemez.

Çözüm: `Optional<T>` struct'ı ile "gönderildi mi" bilgisi taşınır. Bu struct CrudKit.Core'da tanımlıdır.

```csharp
// ---- CrudKit.Core/Models/Optional.cs ----

[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly struct Optional<T>
{
    public bool HasValue { get; }
    public T? Value { get; }

    private Optional(T? value, bool hasValue)
    {
        Value = value;
        HasValue = hasValue;
    }

    /// <summary>JSON'da field yoktu — bu alana dokunma.</summary>
    public static Optional<T> Undefined => new(default, false);

    /// <summary>JSON'da field vardı — bu değeri uygula (null dahil).</summary>
    public static Optional<T> From(T? value) => new(value, true);

    public static implicit operator Optional<T>(T? value) => From(value);
}
```

```csharp
// ---- CrudKit.Core/Serialization/OptionalJsonConverterFactory.cs ----

public class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var innerType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(innerType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

public class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Bu metod çağrıldıysa JSON'da field var demektir → HasValue = true
        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return Optional<T>.From(value);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            JsonSerializer.Serialize(writer, value.Value, options);
    }
}
```

```csharp
// ---- Update DTO'larda kullanım ----

public record UpdateProduct
{
    public Optional<string?> Name { get; init; }
    public Optional<decimal?> Price { get; init; }
    public Optional<int?> Stock { get; init; }
    public Optional<string?> CategoryId { get; init; }
}

// JSON: { "price": 150 }
// → Name.HasValue = false      → dokunma
// → Price.HasValue = true       → 150 yap
// → Stock.HasValue = false      → dokunma
// → CategoryId.HasValue = false → dokunma

// JSON: { "name": null, "price": 150 }
// → Name.HasValue = true, Value = null  → null yap
// → Price.HasValue = true, Value = 150  → 150 yap
```

```csharp
// ---- EfRepo.Update — Optional<T> farkındalığı ----

// Update metodu property'leri dolaşırken:
// 1. Property tipi Optional<T> ise:
//    - HasValue = false → bu property'yi atla (dokunma)
//    - HasValue = true → Value'yu entity'ye uygula (null dahil)
// 2. Property tipi normal ise (geriye uyumluluk):
//    - value != null → uygula
//    - value == null → atla

foreach (var prop in updateDto.GetType().GetProperties())
{
    var value = prop.GetValue(updateDto);
    var propType = prop.PropertyType;

    if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Optional<>))
    {
        var hasValue = (bool)propType.GetProperty("HasValue")!.GetValue(value)!;
        if (!hasValue) continue;

        var innerValue = propType.GetProperty("Value")!.GetValue(value);
        var entityProp = typeof(T).GetProperty(prop.Name);
        entityProp?.SetValue(entity, innerValue);
    }
    else if (value != null)
    {
        var entityProp = typeof(T).GetProperty(prop.Name);
        entityProp?.SetValue(entity, value);
    }
}
```

```csharp
// ---- Testler (CrudKit.EntityFrameworkCore.Tests/Repository/PartialUpdateTests.cs) ----

public class PartialUpdateTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public PartialUpdateTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task ShouldNotTouchUndefinedFields()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Original", "PU-001", 100, 50));
        var updated = await _f.Repo.Update(product.Id, new UpdateProduct { Price = 200 });

        Assert.Equal("Original", updated.Name);  // dokunulmadı
        Assert.Equal(200, updated.Price);          // güncellendi
        Assert.Equal(50, updated.Stock);           // dokunulmadı
    }

    [Fact]
    public async Task ShouldSetFieldToNull_WhenExplicitlySent()
    {
        var product = await _f.Repo.Create(
            new CreateTestProduct("WithCat", "PU-002", 100) { CategoryId = "cat-1" });

        var updated = await _f.Repo.Update(product.Id,
            new UpdateProduct { CategoryId = Optional<string?>.From(null) });

        Assert.Null(updated.CategoryId);           // null yapıldı
        Assert.Equal("WithCat", updated.Name);     // dokunulmadı
    }

    [Fact]
    public async Task ShouldDistinguishNullFromMissing()
    {
        var product = await _f.Repo.Create(
            new CreateTestProduct("Test", "PU-003", 100) { CategoryId = "cat-1" });

        // Undefined — dokunma
        var updated1 = await _f.Repo.Update(product.Id, new UpdateProduct { Price = 200 });
        Assert.Equal("cat-1", updated1.CategoryId);

        // Explicit null — sil
        var updated2 = await _f.Repo.Update(product.Id,
            new UpdateProduct { CategoryId = Optional<string?>.From(null) });
        Assert.Null(updated2.CategoryId);
    }
}

// ---- Testler (CrudKit.Core.Tests/Models/OptionalTests.cs) ----

public class OptionalTests
{
    [Fact]
    public void Undefined_ShouldNotHaveValue()
    {
        var opt = Optional<string>.Undefined;
        Assert.False(opt.HasValue);
    }

    [Fact]
    public void From_WithValue_ShouldHaveValue()
    {
        var opt = Optional<string>.From("hello");
        Assert.True(opt.HasValue);
        Assert.Equal("hello", opt.Value);
    }

    [Fact]
    public void From_WithNull_ShouldHaveValueButValueIsNull()
    {
        var opt = Optional<string?>.From(null);
        Assert.True(opt.HasValue);    // gönderildi
        Assert.Null(opt.Value);        // ama değer null
    }

    [Fact]
    public void ImplicitConversion_ShouldWork()
    {
        Optional<int> opt = 42;
        Assert.True(opt.HasValue);
        Assert.Equal(42, opt.Value);
    }

    [Fact]
    public void JsonDeserialization_ShouldSetHasValue_WhenFieldPresent()
    {
        var json = """{ "Name": "test", "Price": 100 }""";
        var dto = JsonSerializer.Deserialize<UpdateProduct>(json);

        Assert.True(dto!.Name.HasValue);
        Assert.Equal("test", dto.Name.Value);
        Assert.True(dto.Price.HasValue);
        Assert.False(dto.Stock.HasValue);     // JSON'da yoktu
    }

    [Fact]
    public void JsonDeserialization_ShouldSetHasValue_WhenFieldIsNull()
    {
        var json = """{ "Name": null }""";
        var dto = JsonSerializer.Deserialize<UpdateProduct>(json);

        Assert.True(dto!.Name.HasValue);      // gönderildi
        Assert.Null(dto.Name.Value);           // ama null
        Assert.False(dto.Price.HasValue);      // gönderilmedi
    }
}
```

### 11.2 Cascade Soft Delete

Sorun: Master entity soft-delete edildiğinde detail entity'ler orphan kalır. `GET /api/order-items?order_id=123` hâlâ kayıt döner ama master yok. Tutarsız veri.

Çözüm: İki yöntemle cascade tanımı desteklenir — parent tarafında attribute ile veya child tarafında interface ile. CrudKitDbContext ikisini de kontrol eder.

#### Tanımlama — Parent tarafında (Attribute)

```csharp
// ---- CrudKit.Core/Attributes/CascadeSoftDeleteAttribute.cs ----

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CascadeSoftDeleteAttribute : Attribute
{
    /// <summary>Cascade edilecek child entity tipi.</summary>
    public Type ChildType { get; }

    /// <summary>Child entity'deki foreign key property adı.</summary>
    public string ForeignKeyProperty { get; }

    public CascadeSoftDeleteAttribute(Type childType, string foreignKeyProperty)
    {
        ChildType = childType;
        ForeignKeyProperty = foreignKeyProperty;
    }
}
```

```csharp
// Kullanım — parent entity'de child'ları tanımla
[CrudEntity(Table = "orders", SoftDelete = true)]
[CascadeSoftDelete(typeof(OrderItem), "OrderId")]
[CascadeSoftDelete(typeof(OrderNote), "OrderId")]
[CascadeSoftDelete(typeof(OrderAttachment), "OrderId")]
public class Order : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    // ...
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

#### Tanımlama — Child tarafında (Interface)

```csharp
// ---- CrudKit.Core/Interfaces/ICascadeSoftDelete.cs ----

/// <summary>
/// Bu entity'nin parent'ı soft-delete edildiğinde bu entity de soft-delete edilir.
/// </summary>
public interface ICascadeSoftDelete<TParent> where TParent : class, IEntity, ISoftDeletable
{
    /// <summary>Parent'a referans veren foreign key property adı.</summary>
    static abstract string ParentForeignKey { get; }
}
```

```csharp
// Kullanım — child entity'de parent'ı tanımla
[CrudEntity(Table = "order_items", SoftDelete = true)]
public class OrderItem : IEntity, ISoftDeletable, ICascadeSoftDelete<Order>
{
    public static string ParentForeignKey => nameof(OrderId);

    public string Id { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    // ...
    public DateTime? DeletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

#### CrudKitDbContext — Cascade uygulama

```csharp
// BeforeSaveChanges'a eklenen cascade logic:

private void ApplyCascadeSoftDelete()
{
    // Soft-delete edilen entity'leri topla
    var softDeletedEntities = ChangeTracker.Entries()
        .Where(e => e.State == EntityState.Modified
            && e.Entity is ISoftDeletable sd
            && sd.DeletedAt != null
            // Sadece bu SaveChanges'da soft-delete edilenler
            && e.Property(nameof(ISoftDeletable.DeletedAt)).IsModified)
        .ToList();

    foreach (var entry in softDeletedEntities)
    {
        var entityType = entry.Entity.GetType();
        var entityId = (entry.Entity as IEntity)!.Id;
        var deletedAt = (entry.Entity as ISoftDeletable)!.DeletedAt;

        // Yöntem A: Parent üzerindeki [CascadeSoftDelete] attribute'ları
        var cascadeAttributes = entityType.GetCustomAttributes<CascadeSoftDeleteAttribute>();
        foreach (var attr in cascadeAttributes)
        {
            CascadeDeleteChildren(attr.ChildType, attr.ForeignKeyProperty, entityId, deletedAt);
        }

        // Yöntem B: Tüm entity'leri tara, ICascadeSoftDelete<T> implemente edenleri bul
        foreach (var childEntityType in GetAllEntityTypes())
        {
            var cascadeInterface = childEntityType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType
                    && i.GetGenericTypeDefinition() == typeof(ICascadeSoftDelete<>)
                    && i.GetGenericArguments()[0] == entityType);

            if (cascadeInterface != null)
            {
                var fkProperty = (string)childEntityType
                    .GetProperty("ParentForeignKey", BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;
                CascadeDeleteChildren(childEntityType, fkProperty, entityId, deletedAt);
            }
        }
    }
}

private void CascadeDeleteChildren(Type childType, string fkProperty, string parentId, DateTime? deletedAt)
{
    // Child entity'lerin DbSet'ini bul
    var dbSet = GetType().GetProperties()
        .FirstOrDefault(p => p.PropertyType.IsGenericType
            && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
            && p.PropertyType.GetGenericArguments()[0] == childType)
        ?.GetValue(this);

    if (dbSet == null) return;

    // FK ile eşleşen child kayıtları bul
    // Raw SQL ile toplu güncelleme — performans için
    var tableName = Model.FindEntityType(childType)?.GetTableName();
    if (tableName == null) return;

    Database.ExecuteSqlRaw(
        $"UPDATE {tableName} SET deleted_at = {{0}}, updated_at = {{0}} WHERE {fkProperty} = {{1}} AND deleted_at IS NULL",
        deletedAt!, parentId);
}

private IEnumerable<Type> GetAllEntityTypes()
{
    return Model.GetEntityTypes().Select(e => e.ClrType);
}
```

```csharp
// BeforeSaveChanges güncellendi:
private void BeforeSaveChanges()
{
    foreach (var entry in ChangeTracker.Entries<IEntity>())
    {
        switch (entry.State)
        {
            case EntityState.Added:
                // ... mevcut kod (Id, timestamp, tenant)
                break;

            case EntityState.Modified:
                // ... mevcut kod (UpdatedAt, CreatedAt koruması)
                break;

            case EntityState.Deleted:
                if (entry.Entity is ISoftDeletable softDelete)
                {
                    entry.State = EntityState.Modified;
                    softDelete.DeletedAt = DateTime.UtcNow;
                }
                break;
        }
    }

    // Cascade soft delete — soft-delete edilen parent'ların child'larını da sil
    ApplyCascadeSoftDelete();

    // Audit log
    WriteAuditLogs();
}
```

#### Restore — cascade geri yükleme

```csharp
// EfRepo.Restore güncellemesi:
// Parent restore edildiğinde child'ları da restore et

public async Task Restore(string id, CancellationToken ct = default)
{
    var entity = await _db.Set<T>()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw AppError.NotFound();

    if (entity is ISoftDeletable sd)
    {
        sd.DeletedAt = null;

        // Cascade restore
        var cascadeAttributes = typeof(T).GetCustomAttributes<CascadeSoftDeleteAttribute>();
        foreach (var attr in cascadeAttributes)
        {
            var tableName = _db.Model.FindEntityType(attr.ChildType)?.GetTableName();
            if (tableName == null) continue;

            await _db.Database.ExecuteSqlRawAsync(
                $"UPDATE {tableName} SET deleted_at = NULL, updated_at = {{0}} WHERE {attr.ForeignKeyProperty} = {{1}}",
                DateTime.UtcNow, id);
        }

        await _db.SaveChangesAsync(ct);
    }
}
```

#### Derinlemesine cascade (nested)

```csharp
// Order → OrderItem → OrderItemTax
// Order silindiğinde OrderItem silinir
// OrderItem silindiğinde OrderItemTax de silinmeli

// Çözüm: zincir otomatik çalışır çünkü:
// 1. Order soft-delete → ApplyCascadeSoftDelete → OrderItem'lar soft-delete
// 2. OrderItem'lar soft-delete edildiğinde ChangeTracker'da modified olarak görünür
// 3. Ama ExecuteSqlRaw ile güncellendikleri için ChangeTracker'dan geçmezler

// Bu yüzden nested cascade için child'ları da attribute ile tanımla:
[CascadeSoftDelete(typeof(OrderItem), "OrderId")]
public class Order : IEntity, ISoftDeletable { }

[CascadeSoftDelete(typeof(OrderItemTax), "OrderItemId")]
public class OrderItem : IEntity, ISoftDeletable, ICascadeSoftDelete<Order> { }

// ApplyCascadeSoftDelete recursive çalışır:
// Order delete → OrderItem delete → OrderItemTax delete
// ExecuteSqlRaw ile yapıldığı için performanslı

private void CascadeDeleteChildren(Type childType, string fkProperty, string parentId, DateTime? deletedAt)
{
    // ... mevcut child silme kodu ...

    // Recursive: child'ın da cascade tanımları var mı?
    var childCascades = childType.GetCustomAttributes<CascadeSoftDeleteAttribute>();
    foreach (var childCascade in childCascades)
    {
        // Child'ın ID'lerini bul
        var childTableName = Model.FindEntityType(childType)?.GetTableName();
        // Bu child'ların child'larını da sil
        Database.ExecuteSqlRaw(
            $@"UPDATE {Model.FindEntityType(childCascade.ChildType)?.GetTableName()}
               SET deleted_at = {{0}}, updated_at = {{0}}
               WHERE {childCascade.ForeignKeyProperty} IN (
                   SELECT id FROM {childTableName} WHERE {fkProperty} = {{1}}
               ) AND deleted_at IS NULL",
            deletedAt!, parentId);
    }
}
```

#### Testler

```csharp
// ---- CascadeSoftDeleteTests.cs (CrudKit.EntityFrameworkCore.Tests/DbContext/) ----

public class CascadeSoftDeleteTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public CascadeSoftDeleteTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task SoftDeleteParent_ShouldCascadeToChildren()
    {
        // Order + 3 OrderItem oluştur
        var order = new TestOrder { Total = 100 };
        _f.DbContext.Orders.Add(order);
        await _f.DbContext.SaveChangesAsync();

        for (int i = 0; i < 3; i++)
        {
            _f.DbContext.OrderItems.Add(new TestOrderItem
            {
                OrderId = order.Id,
                ProductName = $"Item {i}",
                Quantity = 1,
                Price = 33
            });
        }
        await _f.DbContext.SaveChangesAsync();

        // Order'ı soft-delete et
        _f.DbContext.Orders.Remove(order);
        await _f.DbContext.SaveChangesAsync();

        // Order görünmemeli
        var orders = await _f.DbContext.Orders.ToListAsync();
        Assert.DoesNotContain(orders, o => o.Id == order.Id);

        // OrderItem'lar da görünmemeli
        var items = await _f.DbContext.OrderItems.ToListAsync();
        Assert.DoesNotContain(items, i => i.OrderId == order.Id);

        // Ama IgnoreQueryFilters ile hepsi var
        var allItems = await _f.DbContext.OrderItems
            .IgnoreQueryFilters()
            .Where(i => i.OrderId == order.Id)
            .ToListAsync();
        Assert.Equal(3, allItems.Count);
        Assert.All(allItems, i => Assert.NotNull(i.DeletedAt));
    }

    [Fact]
    public async Task RestoreParent_ShouldCascadeRestoreChildren()
    {
        var order = new TestOrder { Total = 50 };
        _f.DbContext.Orders.Add(order);
        var item = new TestOrderItem { OrderId = order.Id, ProductName = "Test", Quantity = 1, Price = 50 };
        _f.DbContext.OrderItems.Add(item);
        await _f.DbContext.SaveChangesAsync();

        // Soft delete
        _f.DbContext.Orders.Remove(order);
        await _f.DbContext.SaveChangesAsync();

        // Restore
        var repo = new EfRepo<TestDbContext, TestOrder>(_f.DbContext, /* ... */);
        await repo.Restore(order.Id);

        // Order ve item'lar geri gelmeli
        var restoredOrder = await _f.DbContext.Orders.FindAsync(order.Id);
        Assert.NotNull(restoredOrder);
        Assert.Null(restoredOrder.DeletedAt);

        var restoredItems = await _f.DbContext.OrderItems
            .Where(i => i.OrderId == order.Id)
            .ToListAsync();
        Assert.Single(restoredItems);
        Assert.Null(restoredItems[0].DeletedAt);
    }

    [Fact]
    public async Task SoftDeleteParent_ShouldNotAffectOtherParentsChildren()
    {
        // İki farklı order oluştur
        var order1 = new TestOrder { Total = 100 };
        var order2 = new TestOrder { Total = 200 };
        _f.DbContext.Orders.AddRange(order1, order2);
        await _f.DbContext.SaveChangesAsync();

        _f.DbContext.OrderItems.Add(new TestOrderItem { OrderId = order1.Id, ProductName = "A", Quantity = 1, Price = 100 });
        _f.DbContext.OrderItems.Add(new TestOrderItem { OrderId = order2.Id, ProductName = "B", Quantity = 1, Price = 200 });
        await _f.DbContext.SaveChangesAsync();

        // Sadece order1'i sil
        _f.DbContext.Orders.Remove(order1);
        await _f.DbContext.SaveChangesAsync();

        // Order2'nin item'ları etkilenmemeli
        var order2Items = await _f.DbContext.OrderItems
            .Where(i => i.OrderId == order2.Id)
            .ToListAsync();
        Assert.Single(order2Items);
        Assert.Null(order2Items[0].DeletedAt);
    }
}
```

### 11.3 Transaction Scope

Sorun: Hook içinde birden fazla repo çağrısı yapıldığında her biri ayrı `SaveChanges` çağırır. Biri başarılı olup diğeri patlarsa tutarsız veri oluşur. Stok düştü ama bildirim oluşmadı, fatura kaydedildi ama kalemleri kayboldu gibi senaryolar.

Çözüm: CRUD handler'lar her işlemi bir transaction içinde sarar. Hook'lardaki tüm repo çağrıları aynı transaction'da çalışır. Hata olursa toplu rollback.

```csharp
// ---- Handler akışı — transaction sarmalı ----

static async Task<IResult> CreateHandler<TEntity, TCreate>(
    TCreate body,
    IRepo<TEntity> repo,
    ICrudHooks<TEntity>? hooks,
    CrudKitDbContext db,
    AppContext appCtx,
    CancellationToken ct)
    where TEntity : class, IEntity
    where TCreate : class
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);

    try
    {
        // BeforeCreate — validasyon, varsayılan değer atama
        var entity = MapFromDto<TEntity>(body);
        if (hooks != null)
            await hooks.BeforeCreate(entity, appCtx);

        // Create — DB'ye yaz (transaction içinde, commit değil)
        db.Set<TEntity>().Add(entity);
        await db.SaveChangesAsync(ct);

        // AfterCreate — ilişkili kayıtlar, bildirimler
        if (hooks != null)
            await hooks.AfterCreate(entity, appCtx);

        // Her şey başarılı — commit
        await transaction.CommitAsync(ct);
        return Results.Created($"/api/.../{entity.Id}", entity);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}

static async Task<IResult> UpdateHandler<TEntity, TUpdate>(
    string id,
    TUpdate body,
    IRepo<TEntity> repo,
    ICrudHooks<TEntity>? hooks,
    CrudKitDbContext db,
    AppContext appCtx,
    CancellationToken ct)
    where TEntity : class, IEntity
    where TUpdate : class
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);

    try
    {
        var entity = await repo.FindById(id, ct);

        if (hooks != null)
            await hooks.BeforeUpdate(entity, appCtx);

        ApplyUpdates(entity, body);
        await db.SaveChangesAsync(ct);

        if (hooks != null)
            await hooks.AfterUpdate(entity, appCtx);

        await transaction.CommitAsync(ct);
        return Results.Ok(entity);
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}

static async Task<IResult> DeleteHandler<TEntity>(
    string id,
    IRepo<TEntity> repo,
    ICrudHooks<TEntity>? hooks,
    CrudKitDbContext db,
    AppContext appCtx,
    CancellationToken ct)
    where TEntity : class, IEntity
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);

    try
    {
        var entity = await repo.FindById(id, ct);

        if (hooks != null)
            await hooks.BeforeDelete(entity, appCtx);

        db.Set<TEntity>().Remove(entity);
        // Soft delete + cascade → CrudKitDbContext.BeforeSaveChanges'da halledilir
        await db.SaveChangesAsync(ct);

        if (hooks != null)
            await hooks.AfterDelete(entity, appCtx);

        await transaction.CommitAsync(ct);
        return Results.Ok(new { deleted = id });
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

```csharp
// ---- Hook'larda her şey beklenen gibi çalışır ----

public class InvoiceHooks : ICrudHooks<Invoice>
{
    private readonly IRepo<InvoiceLine> _lineRepo;
    private readonly IRepo<StockMovement> _stockRepo;
    private readonly IRepo<Notification> _notifRepo;

    public async Task AfterCreate(Invoice entity, AppContext ctx)
    {
        // 1. Kalemleri oluştur — aynı transaction'da
        await _lineRepo.Create(new CreateInvoiceLine
        {
            InvoiceId = entity.Id,
            Description = "Ürün A",
            Quantity = 2,
            UnitPrice = 100,
            LineTotal = 200
        });

        // 2. Kalemleri sorgula — aynı transaction'da görünür
        var lines = await _lineRepo.FindByField("InvoiceId", entity.Id);
        // ✓ ÇALIŞIR — henüz commit olmadı ama transaction içinde görünür

        // 3. Toplam hesapla
        entity.Total = lines.Sum(l => l.LineTotal);

        // 4. Stok düş
        foreach (var line in lines)
        {
            await _stockRepo.Create(new CreateStockMovement
            {
                ProductId = line.ProductId,
                Quantity = -line.Quantity,
                ReferenceType = "invoice",
                ReferenceId = entity.Id
            });
        }

        // 5. Bildirim — BURADA HATA OLURSA?
        await _notifRepo.Create(new CreateNotification { ... });
        // Hata → catch bloğu → RollbackAsync
        // Fatura, kalemler, stok hareketleri HEPSİ geri alınır
    }
}
```

```csharp
// ---- Neden B (Unit of Work) değil A (Transaction) ----

// B'nin sorunu: repo.Create() aslında DB'ye yazmaz, sadece memory'ye ekler.
// Hook geliştiricisi sonra FindById/FindByField çağırdığında
// boş sonuç alır çünkü veri henüz DB'de yok.
//
// A'da her repo çağrısı gerçekten DB'ye yazıyor
// ama transaction içinde olduğu için commit olmuyor.
// Aynı transaction içindeki sorgular yazılan veriyi görebilir.
// Bu EF Core + ilişkisel DB'nin doğal davranışı — sürpriz yok.
```

#### Nested Transaction — Hook içinden başka entity create etmek

```csharp
// Hook içinde başka bir entity'nin Create handler'ını çağırmıyoruz.
// Doğrudan repo kullanıyoruz. Bu önemli çünkü:
//
// Create handler → transaction açar
//   → Hook çağırır
//     → Hook içinde repo.Create → aynı DbContext → aynı transaction
//     → Hook içinde repo.FindById → aynı DbContext → aynı transaction
//
// Tüm repo'lar aynı scoped DbContext'i kullanıyor (DI sayesinde)
// Bu yüzden ayrı transaction yönetimine gerek yok — hepsi doğal olarak
// aynı transaction'da.
```

#### Testler

```csharp
// ---- TransactionTests.cs (CrudKit.Api.Tests/Endpoints/) ----

public class TransactionTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public TransactionTests(ApiFixture f) => _client = f.Client;

    [Fact]
    public async Task Create_ShouldRollbackAllOnHookFailure()
    {
        // FailingHook: AfterCreate'te exception fırlatır
        // Order oluşturuldu, hook patladı
        // → Order DB'ye yazılmamış olmalı

        var body = new { Total = 100, Status = "new", TriggerError = true };
        var response = await _client.PostAsJsonAsync("/api/orders", body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // Order oluşmamış olmalı
        var listResponse = await _client.GetAsync("/api/orders");
        var result = await listResponse.Content.ReadFromJsonAsync<Paginated<TestOrder>>();
        Assert.DoesNotContain(result!.Data, o => o.Total == 100);
    }

    [Fact]
    public async Task Create_ShouldCommitAllOnSuccess()
    {
        // Hook içinde OrderItem ve StockMovement oluşturuluyor
        // Hepsi başarılı → hepsi commit olmalı

        var body = new { Total = 200, Status = "new" };
        var response = await _client.PostAsJsonAsync("/api/orders", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<TestOrder>();

        // OrderItem'lar da oluşmuş olmalı
        var itemsResponse = await _client.GetAsync($"/api/orders/{order!.Id}/items");
        var items = await itemsResponse.Content.ReadFromJsonAsync<Paginated<TestOrderItem>>();
        Assert.NotEmpty(items!.Data);
    }

    [Fact]
    public async Task Update_ShouldRollbackOnHookFailure()
    {
        // Önce oluştur
        var createResponse = await _client.PostAsJsonAsync("/api/orders",
            new { Total = 300, Status = "new" });
        var order = await createResponse.Content.ReadFromJsonAsync<TestOrder>();

        // Update — hook'ta hata olacak
        var updateResponse = await _client.PutAsJsonAsync($"/api/orders/{order!.Id}",
            new { Status = "error_trigger" });

        Assert.Equal(HttpStatusCode.InternalServerError, updateResponse.StatusCode);

        // Order değişmemiş olmalı
        var getResponse = await _client.GetAsync($"/api/orders/{order.Id}");
        var unchanged = await getResponse.Content.ReadFromJsonAsync<TestOrder>();
        Assert.Equal("new", unchanged!.Status);
    }
}
```

### 11.4 N+1 Sorgu Problemi — Include Stratejisi

Sorun: Generic `Repo<T>` hangi navigation property'leri `Include` edeceğini bilmiyor. Her entity'nin ilişkileri farklı. Include yapılmazsa N+1 sorgu problemi oluşur. 20 order listelerken 41 sorgu çalışır.

Çözüm: İki katmanlı include stratejisi. Entity üzerinde `[DefaultInclude]` attribute ile varsayılan include'lar tanımlanır. Client `?include=` query parametresiyle override edebilir veya genişletebilir.

#### Attribute — DefaultInclude

```csharp
// ---- CrudKit.Core/Attributes/DefaultIncludeAttribute.cs ----

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class DefaultIncludeAttribute : Attribute
{
    /// <summary>
    /// Include edilecek navigation property adı.
    /// Nested include için nokta notasyonu: "Items.Product"
    /// </summary>
    public string NavigationProperty { get; }

    /// <summary>
    /// Sadece GetById'da mı yoksa List'te de mi include edilsin?
    /// List'te çok fazla include performansı düşürebilir.
    /// </summary>
    public IncludeScope Scope { get; set; } = IncludeScope.All;

    public DefaultIncludeAttribute(string navigationProperty)
    {
        NavigationProperty = navigationProperty;
    }
}

public enum IncludeScope
{
    All,          // List + GetById
    DetailOnly    // Sadece GetById
}
```

```csharp
// ---- Entity tanımında kullanım ----

[CrudEntity(Table = "orders", SoftDelete = true)]
[DefaultInclude("Customer")]                                    // List + GetById
[DefaultInclude("Items", Scope = IncludeScope.DetailOnly)]      // Sadece GetById
[DefaultInclude("Items.Product", Scope = IncludeScope.DetailOnly)]  // Nested
public class Order : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;

    // Navigation properties
    public Customer Customer { get; set; } = null!;
    public List<OrderItem> Items { get; set; } = new();

    // ...
}

// GET /api/orders      → Customer include edilir, Items edilmez (performans)
// GET /api/orders/123  → Customer + Items + Items.Product hepsi include edilir
```

#### Query Parameter — ?include=

```csharp
// Client varsayılanı override edebilir veya genişletebilir

// Varsayılan include'ları kullan
GET /api/orders

// Varsayılanları kapat, sadece belirtileni kullan
GET /api/orders?include=items

// Birden fazla include
GET /api/orders?include=customer,items,items.product

// Include'ları tamamen kapat (performans için)
GET /api/orders?include=none

// Nested include
GET /api/orders?include=items.product.category
```

#### IncludeApplier

```csharp
// ---- CrudKit.EntityFrameworkCore/Query/IncludeApplier.cs ----

public static class IncludeApplier
{
    /// <summary>
    /// Entity'ye uygun Include'ları uygular.
    /// Öncelik: query param > attribute default
    /// </summary>
    public static IQueryable<T> Apply<T>(
        IQueryable<T> query,
        string? includeParam,
        bool isDetailQuery) where T : class
    {
        var includes = ResolveIncludes<T>(includeParam, isDetailQuery);

        foreach (var include in includes)
        {
            query = ApplyInclude(query, include);
        }

        return query;
    }

    private static List<string> ResolveIncludes<T>(string? includeParam, bool isDetailQuery)
    {
        // Client "none" gönderdiyse → hiç include yapma
        if (includeParam?.Equals("none", StringComparison.OrdinalIgnoreCase) == true)
            return new();

        // Client include parametresi gönderdiyse → sadece onu kullan
        if (!string.IsNullOrWhiteSpace(includeParam))
        {
            return includeParam
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(i => i.Trim())
                .ToList();
        }

        // Client göndermedi → attribute default'larını kullan
        var attributes = typeof(T).GetCustomAttributes<DefaultIncludeAttribute>();
        return attributes
            .Where(a => a.Scope == IncludeScope.All || (a.Scope == IncludeScope.DetailOnly && isDetailQuery))
            .Select(a => a.NavigationProperty)
            .ToList();
    }

    private static IQueryable<T> ApplyInclude<T>(IQueryable<T> query, string include) where T : class
    {
        // Nested include desteği: "Items.Product.Category"
        var parts = include.Split('.');

        if (parts.Length == 1)
        {
            // Basit include
            return query.Include(BuildExpression<T>(parts[0]));
        }

        // Nested include — EF Core ThenInclude zinciri
        // Reflection ile dinamik oluşturulur
        return ApplyNestedInclude(query, parts);
    }

    private static IQueryable<T> ApplyNestedInclude<T>(IQueryable<T> query, string[] parts) where T : class
    {
        // İlk parça: Include
        // Sonrakiler: ThenInclude
        // EF Core'un Include/ThenInclude API'si generic olduğu için
        // reflection ile çağrılması gerekir

        var entityType = typeof(T);
        var currentType = entityType;

        // EntityFrameworkQueryableExtensions.Include ve ThenInclude
        // metodlarını reflection ile çağır

        // İlk Include
        var firstProp = currentType.GetProperty(parts[0]);
        if (firstProp == null) return query;  // geçersiz property, sessizce atla

        var includeMethod = typeof(EntityFrameworkQueryableExtensions)
            .GetMethods()
            .First(m => m.Name == "Include" && m.GetParameters().Length == 2);

        // ... nested ThenInclude zinciri reflection ile devam eder

        return query;
    }

    private static Expression<Func<T, object>> BuildExpression<T>(string propertyName)
    {
        var param = Expression.Parameter(typeof(T), "e");
        var property = Expression.Property(param, propertyName);
        var converted = Expression.Convert(property, typeof(object));
        return Expression.Lambda<Expression<Func<T, object>>>(converted, param);
    }
}
```

#### Güvenlik — İzin verilen include'lar

```csharp
// Client herhangi bir navigation property isteyebilir.
// Ama bazıları güvenlik riski oluşturabilir:
// GET /api/orders?include=customer.passwordHash  → TEHLIKE!

// Çözüm: sadece entity'de tanımlı navigation property'lere izin ver
// Scalar property'ler include edilemez
// [SkipResponse] olan entity'ler include edilemez

private static bool IsValidInclude<T>(string propertyName)
{
    var prop = typeof(T).GetProperty(propertyName);
    if (prop == null) return false;

    // Scalar mı? (string, int, decimal...) → include edilemez
    if (prop.PropertyType.IsPrimitive || prop.PropertyType == typeof(string) || prop.PropertyType == typeof(decimal))
        return false;

    // Navigation property mi? (class veya collection) → include edilebilir
    return true;
}
```

#### EfRepo entegrasyonu

```csharp
public class EfRepo<TContext, T> : IRepo<T>
    where TContext : CrudKitDbContext
    where T : class, IEntity
{
    public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsNoTracking();

        // Include — List scope
        query = IncludeApplier.Apply(query, listParams.Include, isDetailQuery: false);

        return await _queryBuilder.Apply(query, listParams, ct);
    }

    public async Task<T> FindById(string id, CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsQueryable();

        // Include — Detail scope (daha fazla include)
        query = IncludeApplier.Apply(query, includeParam: null, isDetailQuery: true);

        return await query.FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw AppError.NotFound();
    }
}
```

```csharp
// ---- ListParams'a include eklenmesi ----

public class ListParams
{
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;
    public string? Sort { get; set; }
    public string? Include { get; set; }    // "customer,items" veya "none"
    public Dictionary<string, FilterOp> Filters { get; set; } = new();

    public static ListParams FromQuery(IQueryCollection query)
    {
        // ... mevcut parsing kodu ...
        // include parametresini de parse et
        var include = query["include"].FirstOrDefault();
        // ...
    }
}
```

#### Circular reference koruması

```csharp
// Include yapıldığında circular reference oluşabilir:
// Order.Customer.Orders.Customer.Orders → sonsuz döngü

// Çözüm: JSON serialization'da IgnoreCycles
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// Bu CrudKitAppExtensions.AddCrudKitApi içinde otomatik yapılmalı.
```

#### Testler

```csharp
// ---- IncludeApplierTests.cs (CrudKit.EntityFrameworkCore.Tests/Query/) ----

public class IncludeApplierTests
{
    [Fact]
    public void ShouldResolveDefaultIncludes_ForList()
    {
        // Order → [DefaultInclude("Customer")] scope=All
        // Order → [DefaultInclude("Items")] scope=DetailOnly
        var includes = IncludeApplier.ResolveIncludes<TestOrder>(
            includeParam: null, isDetailQuery: false);

        Assert.Contains("Customer", includes);
        Assert.DoesNotContain("Items", includes);  // DetailOnly
    }

    [Fact]
    public void ShouldResolveDefaultIncludes_ForDetail()
    {
        var includes = IncludeApplier.ResolveIncludes<TestOrder>(
            includeParam: null, isDetailQuery: true);

        Assert.Contains("Customer", includes);
        Assert.Contains("Items", includes);  // DetailOnly → detail'de görünür
    }

    [Fact]
    public void ShouldOverrideDefaults_WhenParamProvided()
    {
        var includes = IncludeApplier.ResolveIncludes<TestOrder>(
            includeParam: "items", isDetailQuery: false);

        Assert.Single(includes);
        Assert.Contains("Items", includes, StringComparer.OrdinalIgnoreCase);
        // Customer yok — client override etti
    }

    [Fact]
    public void ShouldReturnEmpty_WhenNoneProvided()
    {
        var includes = IncludeApplier.ResolveIncludes<TestOrder>(
            includeParam: "none", isDetailQuery: true);

        Assert.Empty(includes);
    }

    [Fact]
    public void ShouldParseMultipleIncludes()
    {
        var includes = IncludeApplier.ResolveIncludes<TestOrder>(
            includeParam: "customer,items,items.product", isDetailQuery: false);

        Assert.Equal(3, includes.Count);
    }

    [Fact]
    public void ShouldIgnoreInvalidProperties()
    {
        var query = new List<TestOrder>().AsQueryable();
        // "nonexistent" property sessizce atlanmalı, hata vermemeli
        var result = IncludeApplier.Apply(query, "customer,nonexistent", isDetailQuery: false);
        Assert.NotNull(result);
    }
}
```

### 11.5 Migration Stratejisi

Sorun: CrudKit'in kendi tabloları var (`__crud_audit_logs`, `__crud_sequences`). Bunlar nasıl oluşturulacak? `EnsureCreated` production'da kullanılmaz — tablo yapısı değiştiğinde güncelleme yapmaz, rollback yok.

Çözüm: Standart EF Core Migrations. CrudKit tabloları `CrudKitDbContext`'te tanımlı, kullanıcı `CrudKitDbContext`'ten türetiyor. `dotnet ef migrations add` çalıştırınca CrudKit tabloları otomatik migration'a dahil olur. Sıfır ekstra efor.

#### CrudKit hiçbir migration komutu çalıştırmaz

```
CrudKit'in yapması gereken:       → Tabloları OnModelCreating'de tanımla (zaten yapıyor)
CrudKit'in YAPMAMASI gereken:     → EnsureCreated, Migrate, veya kendi migration'ını çalıştırmak
Kullanıcının yapması gereken:     → dotnet ef migrations add / dotnet ef database update
```

#### Kullanıcı tarafı — standart EF Core workflow

```bash
# İlk migration — CrudKit tabloları + kullanıcı tabloları hep birlikte
dotnet ef migrations add InitialCreate -c AppDbContext -o Migrations

# Üretilen migration şunları içerir:
#   - __crud_audit_logs tablosu (CrudKit)
#   - __crud_sequences tablosu (CrudKit)
#   - users, products, orders... (kullanıcı entity'leri)
#   - Unique index'ler, partial index'ler (CrudKit attribute'larından)
#   - Hepsi tek migration'da

# Veritabanını güncelle
dotnet ef database update -c AppDbContext

# Yeni entity eklendiğinde
dotnet ef migrations add AddInvoiceEntity -c AppDbContext

# CrudKit güncellendiğinde (yeni kolon, yeni tablo)
# Kullanıcı tekrar migration ekler — EF Core farkı algılar
dotnet ef migrations add CrudKitUpgrade -c AppDbContext
```

#### CrudKitDbContext — migration dostu tablo tanımları

```csharp
// CrudKitDbContext.OnModelCreating'de tanımlanan tablolar
// EF Core migration sistemi bunları otomatik algılar

modelBuilder.Entity<AuditLogEntry>(b =>
{
    b.ToTable("__crud_audit_logs");
    b.HasKey(e => e.Id);
    b.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
    b.Property(e => e.EntityId).HasMaxLength(50).IsRequired();
    b.Property(e => e.Action).HasMaxLength(20).IsRequired();
    b.Property(e => e.UserId).HasMaxLength(50);
    b.Property(e => e.OldValues).HasColumnType("text");
    b.Property(e => e.NewValues).HasColumnType("text");
    b.Property(e => e.ChangedFields).HasColumnType("text");
    b.HasIndex(e => new { e.EntityType, e.EntityId });
    b.HasIndex(e => e.Timestamp);
    b.HasIndex(e => e.UserId);
});

modelBuilder.Entity<SequenceEntry>(b =>
{
    b.ToTable("__crud_sequences");
    b.HasKey(e => e.Id);
    b.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
    b.Property(e => e.TenantId).HasMaxLength(50).IsRequired();
    b.Property(e => e.Year).HasMaxLength(4).IsRequired();
    b.Property(e => e.CurrentVal).IsRequired();
    b.HasIndex(e => new { e.EntityType, e.TenantId, e.Year }).IsUnique();
});
```

#### Startup'ta otomatik migrate — opsiyonel

```csharp
// Production'da migration genelde CI/CD pipeline'da çalıştırılır.
// Ama geliştirme ortamında otomatik migrate kolaylık sağlar.

public static WebApplication UseCrudKit(this WebApplication app)
{
    app.UseMiddleware<AppErrorFilter>();

    // Geliştirme ortamında otomatik migrate (opsiyonel)
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrudKitDbContext>();
        db.Database.Migrate();  // EnsureCreated DEĞİL, Migrate
    }

    return app;
}
```

#### CrudKit versiyon güncelleme senaryosu

```
Senaryo: CrudKit v1.2'de AuditLogEntry'ye IpAddress kolonu eklendi

1. Kullanıcı CrudKit NuGet paketini günceller
2. CrudKit'in OnModelCreating'de yeni kolon tanımı var
3. Kullanıcı çalıştırır: dotnet ef migrations add CrudKitV12Upgrade
4. EF Core farkı algılar: "__crud_audit_logs tablosuna IpAddress kolonu ekle"
5. Migration oluşturulur, review edilir, uygulanır
6. Rollback gerekirse: dotnet ef database update PreviousMigration

CrudKit hiçbir zaman kendi migration'ını zorlamaz.
Kullanıcı ne zaman ve nasıl migrate edeceğine kendisi karar verir.
```

#### Testler

```csharp
// ---- MigrationTests.cs (CrudKit.EntityFrameworkCore.Tests/DbContext/) ----

public class MigrationTests
{
    [Fact]
    public void CrudKitTables_ShouldBeIncludedInModel()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new TestDbContext(options, new FakeCurrentUser());

        var model = db.Model;

        // CrudKit tabloları model'de tanımlı olmalı
        Assert.NotNull(model.FindEntityType(typeof(AuditLogEntry)));
        Assert.NotNull(model.FindEntityType(typeof(SequenceEntry)));
    }

    [Fact]
    public void CrudKitTables_ShouldHaveCorrectTableNames()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new TestDbContext(options, new FakeCurrentUser());

        var auditTable = db.Model.FindEntityType(typeof(AuditLogEntry))?.GetTableName();
        var seqTable = db.Model.FindEntityType(typeof(SequenceEntry))?.GetTableName();

        Assert.Equal("__crud_audit_logs", auditTable);
        Assert.Equal("__crud_sequences", seqTable);
    }

    [Fact]
    public async Task Migrate_ShouldCreateAllTables()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new TestDbContext(options, new FakeCurrentUser());
        await db.Database.OpenConnectionAsync();

        // Migrate — EnsureCreated değil
        await db.Database.EnsureCreatedAsync();
        // Not: gerçek migration testi için migration assembly gerekir
        // Bu test sadece tablo yapısının doğruluğunu kontrol eder

        // CrudKit tabloları oluşmuş olmalı
        await db.AuditLogs.ToListAsync();      // hata vermemeli
        await db.Sequences.ToListAsync();       // hata vermemeli

        // Kullanıcı tabloları da oluşmuş olmalı
        await db.Products.ToListAsync();        // hata vermemeli
    }

    [Fact]
    public void UniqueIndexes_ShouldBeInModel()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new TestDbContext(options, new FakeCurrentUser());

        var productType = db.Model.FindEntityType(typeof(TestProduct));
        var skuIndex = productType?.GetIndexes()
            .FirstOrDefault(i => i.Properties.Any(p => p.Name == "Sku"));

        Assert.NotNull(skuIndex);
        Assert.True(skuIndex.IsUnique);
    }

    [Fact]
    public void SequenceTable_ShouldHaveUniqueConstraint()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new TestDbContext(options, new FakeCurrentUser());

        var seqType = db.Model.FindEntityType(typeof(SequenceEntry));
        var uniqueIndex = seqType?.GetIndexes()
            .FirstOrDefault(i => i.IsUnique
                && i.Properties.Any(p => p.Name == "EntityType")
                && i.Properties.Any(p => p.Name == "TenantId")
                && i.Properties.Any(p => p.Name == "Year"));

        Assert.NotNull(uniqueIndex);
    }
}
```

### 11.6 Idempotency

Sorun: Client aynı POST isteğini iki kez gönderirse (network timeout, retry, webhook tekrar tetiklenmesi) iki aynı kayıt oluşur. ERP'de duplike fatura, duplike sipariş kabul edilemez.

Çözüm: `Idempotency-Key` header'ı ile istek takibi. Client benzersiz bir key gönderir, sunucu bu key ile işlemi kaydeder. Aynı key tekrar gelirse işlemi tekrar çalıştırmaz, önceki response'u döner.

#### Akış

```
İlk istek:
  Client → POST /api/orders  (Idempotency-Key: abc-123)
  Server → key yok → işlemi çalıştır → response'u kaydet → 201 döndür

Tekrar istek (retry):
  Client → POST /api/orders  (Idempotency-Key: abc-123)
  Server → key var → kaydedilen response'u döndür → 201 (aynı body)

Farklı istek:
  Client → POST /api/orders  (Idempotency-Key: def-456)
  Server → key yok → yeni işlem → 201
```

#### IdempotencyRecord

```csharp
// ---- CrudKit.EntityFrameworkCore/Models/IdempotencyRecord.cs ----

public class IdempotencyRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Client'ın gönderdiği idempotency key.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Hangi endpoint'e gönderildi.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>HTTP method (POST, PUT, DELETE).</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>Kaydedilen response body (JSON).</summary>
    public string ResponseBody { get; set; } = string.Empty;

    /// <summary>Response header'ları (JSON).</summary>
    public string? ResponseHeaders { get; set; }

    /// <summary>İşlem yapan kullanıcı.</summary>
    public string? UserId { get; set; }

    /// <summary>Tenant ID.</summary>
    public string? TenantId { get; set; }

    /// <summary>Ne zaman oluşturuldu.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Ne zamana kadar geçerli. Süresi dolunca temizlenir.</summary>
    public DateTime ExpiresAt { get; set; }
}
```

#### CrudKitDbContext — tablo tanımı

```csharp
// OnModelCreating'e eklenir:

modelBuilder.Entity<IdempotencyRecord>(b =>
{
    b.ToTable("__crud_idempotency");
    b.HasKey(e => e.Id);
    b.Property(e => e.Key).HasMaxLength(100).IsRequired();
    b.Property(e => e.Path).HasMaxLength(500).IsRequired();
    b.Property(e => e.Method).HasMaxLength(10).IsRequired();
    b.Property(e => e.ResponseBody).HasColumnType("text");
    b.HasIndex(e => new { e.Key, e.TenantId }).IsUnique();
    b.HasIndex(e => e.ExpiresAt);  // cleanup için
});
```

#### IdempotencyFilter — Endpoint Filter

```csharp
// ---- CrudKit.Api/Filters/IdempotencyFilter.cs ----

public class IdempotencyFilter : IEndpointFilter
{
    private readonly CrudKitDbContext _db;
    private readonly ICurrentUser _currentUser;

    public IdempotencyFilter(CrudKitDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var httpContext = ctx.HttpContext;
        var method = httpContext.Request.Method;

        // Sadece POST, PUT, DELETE için — GET idempotent zaten
        if (method == "GET") return await next(ctx);

        // Idempotency-Key header'ı var mı?
        var key = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();

        // Key yoksa normal devam et — idempotency opsiyonel
        if (string.IsNullOrEmpty(key)) return await next(ctx);

        var tenantId = _currentUser.TenantId;
        var path = httpContext.Request.Path.Value ?? "";

        // Bu key daha önce işlendi mi?
        var existing = await _db.Set<IdempotencyRecord>()
            .FirstOrDefaultAsync(r =>
                r.Key == key
                && r.TenantId == tenantId
                && r.ExpiresAt > DateTime.UtcNow);

        if (existing != null)
        {
            // Daha önce işlendi — kaydedilen response'u döndür
            httpContext.Response.StatusCode = existing.StatusCode;
            httpContext.Response.Headers["X-Idempotency-Replayed"] = "true";

            if (!string.IsNullOrEmpty(existing.ResponseHeaders))
            {
                var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(existing.ResponseHeaders);
                if (headers != null)
                {
                    foreach (var (headerKey, headerValue) in headers)
                        httpContext.Response.Headers[headerKey] = headerValue;
                }
            }

            return Results.Content(existing.ResponseBody, "application/json", statusCode: existing.StatusCode);
        }

        // İşlenme sırasında aynı key'in tekrar gelmesini engelle — lock
        // Race condition: iki istek aynı anda gelirse ikisi de "existing yok" der
        // Çözüm: DB'ye boş kayıt ekle, unique constraint ile ikinci istek hata alır
        var lockRecord = new IdempotencyRecord
        {
            Key = key,
            TenantId = tenantId,
            Path = path,
            Method = method,
            StatusCode = 0,            // henüz işlenmedi
            ResponseBody = "",
            UserId = _currentUser.Id,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
        };

        try
        {
            _db.Set<IdempotencyRecord>().Add(lockRecord);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique constraint — aynı key zaten eklendi (race condition)
            // Kısa bekle, tekrar dene
            await Task.Delay(100);
            existing = await _db.Set<IdempotencyRecord>()
                .FirstOrDefaultAsync(r => r.Key == key && r.TenantId == tenantId);
            if (existing != null && existing.StatusCode > 0)
            {
                return Results.Content(existing.ResponseBody, "application/json", statusCode: existing.StatusCode);
            }
            // Hâlâ işleniyor — 409 Conflict
            return Results.Problem(statusCode: 409, title: "Request is being processed");
        }

        // İşlemi çalıştır
        var response = await next(ctx);

        // Response'u kaydet
        var statusCode = httpContext.Response.StatusCode;
        var responseBody = "";

        if (response is IValueHttpResult valueResult)
        {
            responseBody = JsonSerializer.Serialize(valueResult.Value);
            statusCode = valueResult is IStatusCodeHttpResult statusResult
                ? statusResult.StatusCode ?? 200
                : 200;
        }

        lockRecord.StatusCode = statusCode;
        lockRecord.ResponseBody = responseBody;
        await _db.SaveChangesAsync();

        return response;
    }
}
```

#### CrudEndpointMapper — otomatik ekleme

```csharp
// POST, PUT, DELETE endpoint'lerine otomatik eklenir:

group.MapPost("/", CreateHandler<TEntity, TCreate>)
    .AddEndpointFilter<ValidationFilter<TCreate>>()
    .AddEndpointFilter<IdempotencyFilter>();          // otomatik

group.MapPut("/{id}", UpdateHandler<TEntity, TUpdate>)
    .AddEndpointFilter<ValidationFilter<TUpdate>>()
    .AddEndpointFilter<IdempotencyFilter>();          // otomatik

group.MapDelete("/{id}", DeleteHandler<TEntity>)
    .AddEndpointFilter<IdempotencyFilter>();          // otomatik

// GET endpoint'lerine eklenmez — zaten idempotent
```

#### Client tarafı kullanım

```
POST /api/orders
Idempotency-Key: ord-20260403-abc123
Content-Type: application/json

{ "total": 500, "customerId": "cust-1" }

# İlk istek → 201 Created + order body
# Retry     → 201 Created + aynı body (X-Idempotency-Replayed: true)
```

```csharp
// Client tarafında key üretimi:
// GUID, UUID, veya anlamlı bir format kullanılabilir
// Aynı iş mantığı için aynı key gönderilmeli

var idempotencyKey = $"create-order-{customerId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
// veya
var idempotencyKey = Guid.NewGuid().ToString();
```

#### Cleanup — expired kayıtları temizle

```csharp
// BackgroundService ile periyodik temizlik

public class IdempotencyCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CrudKitDbContext>();

            // 24 saatten eski kayıtları sil
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM __crud_idempotency WHERE expires_at < {0}",
                DateTime.UtcNow);

            await Task.Delay(TimeSpan.FromHours(1), ct);
        }
    }
}

// DI kayıt (AddCrudKit içinde):
services.AddHostedService<IdempotencyCleanupService>();
```

#### Konfigürasyon

```csharp
public class CrudKitApiOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public bool EnableSchemaEndpoint { get; set; } = true;
    public bool EnableSwagger { get; set; } = true;
    public string ApiPrefix { get; set; } = "/api";

    // Idempotency
    public bool EnableIdempotency { get; set; } = true;
    public TimeSpan IdempotencyKeyExpiry { get; set; } = TimeSpan.FromHours(24);
}
```

#### Testler

```csharp
// ---- IdempotencyTests.cs (CrudKit.Api.Tests/Filters/) ----

public class IdempotencyTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public IdempotencyTests(ApiFixture f) => _client = f.Client;

    [Fact]
    public async Task SameKey_ShouldReturnSameResponse()
    {
        var key = Guid.NewGuid().ToString();
        var body = new { Name = "Idempotent", Sku = "IDP-001", Price = 100 };

        // İlk istek
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", key } }
        };
        var response1 = await _client.SendAsync(request1);
        var product1 = await response1.Content.ReadFromJsonAsync<TestProduct>();

        // Aynı key ile tekrar
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", key } }
        };
        var response2 = await _client.SendAsync(request2);
        var product2 = await response2.Content.ReadFromJsonAsync<TestProduct>();

        // Aynı response
        Assert.Equal(response1.StatusCode, response2.StatusCode);
        Assert.Equal(product1!.Id, product2!.Id);
        Assert.Equal("true", response2.Headers.GetValues("X-Idempotency-Replayed").First());
    }

    [Fact]
    public async Task DifferentKeys_ShouldCreateDifferentRecords()
    {
        var body = new { Name = "Different", Sku = "DIF-001", Price = 100 };

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        var response1 = await _client.SendAsync(request1);
        var product1 = await response1.Content.ReadFromJsonAsync<TestProduct>();

        body = new { Name = "Different", Sku = "DIF-002", Price = 100 };
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", Guid.NewGuid().ToString() } }
        };
        var response2 = await _client.SendAsync(request2);
        var product2 = await response2.Content.ReadFromJsonAsync<TestProduct>();

        Assert.NotEqual(product1!.Id, product2!.Id);
    }

    [Fact]
    public async Task NoKey_ShouldProcessNormally()
    {
        var body = new { Name = "NoKey", Sku = "NK-001", Price = 100 };

        // Key yok — normal create
        var response = await _client.PostAsJsonAsync("/api/products", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetRequest_ShouldIgnoreIdempotencyKey()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products")
        {
            Headers = { { "Idempotency-Key", "should-be-ignored" } }
        };
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Idempotency-Replayed"));
    }

    [Fact]
    public async Task ExpiredKey_ShouldProcessAgain()
    {
        // Bu test gerçek expiry ile yapılması zor
        // Integration test'te DB'deki kaydın ExpiresAt'ını geçmişe set ederek test edilir
    }

    [Fact]
    public async Task DifferentTenants_ShouldHaveSeparateKeys()
    {
        var key = "shared-key-123";
        var body = new { Name = "Tenant", Sku = "TN-001", Price = 100 };

        // Tenant A ile
        _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant-A");
        var request1 = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", key } }
        };
        var response1 = await _client.SendAsync(request1);

        // Tenant B ile aynı key — farklı tenant, farklı işlem
        _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "tenant-B");
        body = new { Name = "Tenant", Sku = "TN-002", Price = 100 };
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/api/products")
        {
            Content = JsonContent.Create(body),
            Headers = { { "Idempotency-Key", key } }
        };
        var response2 = await _client.SendAsync(request2);

        var product1 = await response1.Content.ReadFromJsonAsync<TestProduct>();
        var product2 = await response2.Content.ReadFromJsonAsync<TestProduct>();
        Assert.NotEqual(product1!.Id, product2!.Id);
    }
}
```

### 11.7 Bulk Operations

Sorun: 10.000 kaydı tek tek Update/Delete etmek performans felaketi. EF Core 7+ `ExecuteUpdate` ve `ExecuteDelete` ile toplu işlemleri tek SQL'de yapabilir. CrudKit bunu endpoint olarak sunmalı ve `[CrudEntity]` attribute'u ile açılıp kapanabilmeli.

#### CrudEntity Attribute — bulk ayarları

```csharp
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

    // Bulk operations
    public bool EnableBulkUpdate { get; set; } = false;   // varsayılan kapalı
    public bool EnableBulkDelete { get; set; } = false;   // varsayılan kapalı
}
```

```csharp
// Kullanım — entity bazında açılır

[CrudEntity(Table = "products", SoftDelete = true, EnableBulkUpdate = true, EnableBulkDelete = true)]
public class Product : IEntity, ISoftDeletable { ... }

[CrudEntity(Table = "orders", SoftDelete = true, Audit = true)]
public class Order : IEntity, ISoftDeletable, IAuditable { ... }
// Order'da bulk yok — tehlikeli, tek tek silinmeli

[CrudEntity(Table = "logs", EnableBulkDelete = true)]
public class SystemLog : IEntity { ... }
// Log'da sadece bulk delete — toplu temizlik için
```

#### IRepo<T> — bulk metodları

```csharp
public interface IRepo<T> where T : class, IEntity
{
    // ... mevcut metodlar ...

    /// <summary>
    /// Filtreye uyan kayıtları toplu günceller.
    /// Tek SQL çalıştırır — SaveChanges/hook/audit tetiklemez.
    /// </summary>
    Task<int> BulkUpdate(
        Dictionary<string, FilterOp> filters,
        Dictionary<string, object?> updates,
        CancellationToken ct = default);

    /// <summary>
    /// Filtreye uyan kayıtları toplu siler.
    /// Soft delete entity'lerde toplu soft delete yapar.
    /// Tek SQL çalıştırır — SaveChanges/hook/audit tetiklemez.
    /// </summary>
    Task<int> BulkDelete(
        Dictionary<string, FilterOp> filters,
        CancellationToken ct = default);

    /// <summary>
    /// Filtreye uyan kayıt sayısını döner (bulk öncesi preview).
    /// </summary>
    Task<long> BulkCount(
        Dictionary<string, FilterOp> filters,
        CancellationToken ct = default);
}
```

#### EfRepo — bulk implementasyonu

```csharp
public class EfRepo<TContext, T> : IRepo<T>
    where TContext : CrudKitDbContext
    where T : class, IEntity
{
    public async Task<int> BulkUpdate(
        Dictionary<string, FilterOp> filters,
        Dictionary<string, object?> updates,
        CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsQueryable();

        // Tenant filtresi — global query filter zaten uygulanır
        // Filtreleri uygula
        query = _filterApplier.ApplyAll(query, filters);

        // SetProperty çağrıları oluştur
        // EF Core ExecuteUpdate ile tek SQL
        return await query.ExecuteUpdateAsync(setters =>
        {
            foreach (var (field, value) in updates)
            {
                var prop = typeof(T).GetProperty(field);
                if (prop == null) continue;

                // [Protected] ve [SkipUpdate] alanlar güncellenemez
                if (prop.GetCustomAttribute<ProtectedAttribute>() != null) continue;
                if (prop.GetCustomAttribute<SkipUpdateAttribute>() != null) continue;

                // Reflection ile SetProperty çağrısı
                setters.SetProperty(
                    BuildPropertyExpression<T>(field),
                    value);
            }

            // updated_at otomatik güncelle
            setters.SetProperty(e => e.UpdatedAt, DateTime.UtcNow);
        }, ct);
    }

    public async Task<int> BulkDelete(
        Dictionary<string, FilterOp> filters,
        CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsQueryable();
        query = _filterApplier.ApplyAll(query, filters);

        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            // Soft delete — toplu UPDATE
            return await query.ExecuteUpdateAsync(setters =>
            {
                setters.SetProperty(
                    e => ((ISoftDeletable)e).DeletedAt,
                    DateTime.UtcNow);
                setters.SetProperty(e => e.UpdatedAt, DateTime.UtcNow);
            }, ct);
        }
        else
        {
            // Hard delete — toplu DELETE
            return await query.ExecuteDeleteAsync(ct);
        }
    }

    public async Task<long> BulkCount(
        Dictionary<string, FilterOp> filters,
        CancellationToken ct = default)
    {
        var query = _db.Set<T>().AsQueryable();
        query = _filterApplier.ApplyAll(query, filters);
        return await query.LongCountAsync(ct);
    }
}
```

#### Endpoint'ler

```csharp
// CrudEndpointMapper'a eklenen:

public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate>(
    this WebApplication app,
    string route,
    Action<CrudEndpointOptions>? configure = null)
    where TEntity : class, IEntity
    where TCreate : class
    where TUpdate : class
{
    var group = app.MapGroup($"/api/{route}");
    var crudAttr = typeof(TEntity).GetCustomAttribute<CrudEntityAttribute>();

    // ... mevcut CRUD endpoint'ler ...

    // Bulk endpoints — attribute'da açıksa
    if (crudAttr?.EnableBulkUpdate == true)
    {
        group.MapPut("/bulk", BulkUpdateHandler<TEntity>)
            .RequireAuth();
    }

    if (crudAttr?.EnableBulkDelete == true)
    {
        group.MapDelete("/bulk", BulkDeleteHandler<TEntity>)
            .RequireAuth();

        group.MapPost("/bulk/count", BulkCountHandler<TEntity>)
            .RequireAuth();
    }

    return group;
}
```

#### Handler'lar

```csharp
// ---- BulkUpdateHandler ----
// PUT /api/products/bulk
// Body:
// {
//   "filters": { "category_id": "eq:cat-1", "price": "lt:100" },
//   "updates": { "price": 150, "is_active": true }
// }
// Response: { "affected": 342 }

static async Task<IResult> BulkUpdateHandler<TEntity>(
    BulkUpdateRequest body,
    IRepo<TEntity> repo,
    CancellationToken ct) where TEntity : class, IEntity
{
    // Güvenlik: filtre zorunlu — filtresiz toplu güncelleme yapılamaz
    if (body.Filters == null || body.Filters.Count == 0)
        return Results.Problem(statusCode: 400, title: "Bulk update requires at least one filter");

    if (body.Updates == null || body.Updates.Count == 0)
        return Results.Problem(statusCode: 400, title: "No fields to update");

    var affected = await repo.BulkUpdate(body.Filters, body.Updates, ct);
    return Results.Ok(new { affected });
}

// ---- BulkDeleteHandler ----
// DELETE /api/products/bulk
// Body:
// {
//   "filters": { "is_active": "eq:false", "updated_at": "lt:2025-01-01" }
// }
// Response: { "affected": 1205 }

static async Task<IResult> BulkDeleteHandler<TEntity>(
    BulkDeleteRequest body,
    IRepo<TEntity> repo,
    CancellationToken ct) where TEntity : class, IEntity
{
    // Güvenlik: filtre zorunlu
    if (body.Filters == null || body.Filters.Count == 0)
        return Results.Problem(statusCode: 400, title: "Bulk delete requires at least one filter");

    var affected = await repo.BulkDelete(body.Filters, ct);
    return Results.Ok(new { affected });
}

// ---- BulkCountHandler — preview ----
// POST /api/products/bulk/count
// Body:
// {
//   "filters": { "is_active": "eq:false", "updated_at": "lt:2025-01-01" }
// }
// Response: { "count": 1205 }
// Client silmeden önce kaç kayıt etkileneceğini görebilir

static async Task<IResult> BulkCountHandler<TEntity>(
    BulkCountRequest body,
    IRepo<TEntity> repo,
    CancellationToken ct) where TEntity : class, IEntity
{
    var count = await repo.BulkCount(body.Filters, ct);
    return Results.Ok(new { count });
}
```

#### Request modelleri

```csharp
public record BulkUpdateRequest
{
    public Dictionary<string, string> Filters { get; init; } = new();   // FilterOp.Parse ile parse edilir
    public Dictionary<string, object?> Updates { get; init; } = new();
}

public record BulkDeleteRequest
{
    public Dictionary<string, string> Filters { get; init; } = new();
}

public record BulkCountRequest
{
    public Dictionary<string, string> Filters { get; init; } = new();
}
```

#### Güvenlik kuralları

```
1. Filtre zorunlu — filtresiz bulk operasyon yapılamaz (tüm tabloyu silmeyi engelle)
2. Auth zorunlu — bulk endpoint'ler RequireAuth filter'ı ile korunur
3. [Protected] alanlar bulk update'de de güncellenemez
4. Varsayılan kapalı — entity bazında açıkça enable edilmeli
5. Soft delete entity'lerde bulk delete → bulk soft delete
6. Hook/audit tetiklenmez — performans için bilinçli karar
   Audit gerekiyorsa kullanıcı hook ile kendi yazar
7. Cascade soft delete tetiklenmez — tek tablo üzerinde çalışır
   Cascade gerekiyorsa kullanıcı ayrı bulk çağrısı yapar
```

#### Hook/Audit uyarısı

```csharp
// ÖNEMLI: Bulk operasyonlar ExecuteUpdate/ExecuteDelete kullanır.
// Bu EF Core ChangeTracker'ı atlar, yani:
//   - ICrudHooks tetiklenmez
//   - IAuditable audit log yazılmaz
//   - CascadeSoftDelete çalışmaz
//   - BeforeSaveChanges çağrılmaz
//
// Bu bilinçli bir trade-off: 10.000 kayıt için hook çalıştırmak
// zaten mantıksız. Bulk = performans öncelikli.
//
// Audit gerekiyorsa: kullanıcı bulk sonrası kendi audit kaydını yazar
// Cascade gerekiyorsa: child entity için de ayrı bulk çağrısı yapar
```

#### Üretilen endpoint'ler (bulk açıksa)

```
── products (EnableBulkUpdate + EnableBulkDelete) ──
GET    /api/products                     Listele
POST   /api/products                     Oluştur
GET    /api/products/{id}                Tek kayıt
PUT    /api/products/{id}                Güncelle
DELETE /api/products/{id}                Sil
POST   /api/products/{id}/restore        Geri yükle
PUT    /api/products/bulk                Toplu güncelle      ← yeni
DELETE /api/products/bulk                Toplu sil           ← yeni
POST   /api/products/bulk/count          Etkilenen sayısı    ← yeni

── orders (bulk kapalı) ──
GET    /api/orders                       Listele
POST   /api/orders                       Oluştur
GET    /api/orders/{id}                  Tek kayıt
PUT    /api/orders/{id}                  Güncelle
DELETE /api/orders/{id}                  Sil
                                         bulk endpoint yok
```

#### Testler

```csharp
// ---- BulkOperationTests.cs (CrudKit.Api.Tests/Endpoints/) ----

public class BulkOperationTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public BulkOperationTests(ApiFixture f) => _client = f.Client;

    [Fact]
    public async Task BulkUpdate_ShouldUpdateMatchingRecords()
    {
        // 10 ürün oluştur, 5'i category-A
        for (int i = 0; i < 10; i++)
        {
            await _client.PostAsJsonAsync("/api/products", new
            {
                Name = $"Product {i}",
                Sku = $"BU-{i:D3}",
                Price = 100,
                CategoryId = i < 5 ? "cat-A" : "cat-B"
            });
        }

        // category-A olanların fiyatını 200 yap
        var body = new
        {
            filters = new { category_id = "eq:cat-A" },
            updates = new { Price = 200 }
        };
        var response = await _client.PutAsJsonAsync("/api/products/bulk", body);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, result.GetProperty("affected").GetInt32());

        // Doğrula
        var listResponse = await _client.GetAsync("/api/products?category_id=eq:cat-A");
        var products = await listResponse.Content.ReadFromJsonAsync<Paginated<TestProduct>>();
        Assert.All(products!.Data, p => Assert.Equal(200, p.Price));
    }

    [Fact]
    public async Task BulkDelete_ShouldSoftDeleteMatchingRecords()
    {
        // Ürünler oluştur
        for (int i = 0; i < 5; i++)
        {
            await _client.PostAsJsonAsync("/api/products", new
            {
                Name = $"DeleteMe {i}",
                Sku = $"BD-{i:D3}",
                Price = 10,
                IsActive = false
            });
        }

        // is_active=false olanları toplu sil
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/products/bulk")
        {
            Content = JsonContent.Create(new
            {
                filters = new { is_active = "eq:false" }
            })
        };
        var response = await _client.SendAsync(request);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.GetProperty("affected").GetInt32() >= 5);

        // Listeden kaybolmuş olmalı (soft delete)
        var listResponse = await _client.GetAsync("/api/products?is_active=eq:false");
        var products = await listResponse.Content.ReadFromJsonAsync<Paginated<TestProduct>>();
        Assert.Empty(products!.Data);
    }

    [Fact]
    public async Task BulkCount_ShouldReturnMatchingCount()
    {
        var body = new { filters = new { price = "gte:100" } };
        var response = await _client.PostAsJsonAsync("/api/products/bulk/count", body);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result.GetProperty("count").GetInt64() >= 0);
    }

    [Fact]
    public async Task BulkUpdate_ShouldRejectWithoutFilter()
    {
        var body = new
        {
            filters = new { },
            updates = new { Price = 0 }
        };
        var response = await _client.PutAsJsonAsync("/api/products/bulk", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_ShouldRejectWithoutFilter()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/products/bulk")
        {
            Content = JsonContent.Create(new { filters = new { } })
        };
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkUpdate_ShouldNotUpdateProtectedFields()
    {
        // [Protected] alan bulk update'de de güncellenemez
        var body = new
        {
            filters = new { price = "gte:0" },
            updates = new { Status = "hacked" }  // [Protected] alan
        };
        var response = await _client.PutAsJsonAsync("/api/products/bulk", body);

        // İşlem çalışır ama protected alan atlanır
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BulkEndpoints_ShouldNotExistWhenDisabled()
    {
        // Order entity'de bulk kapalı
        var response = await _client.PutAsJsonAsync("/api/orders/bulk",
            new { filters = new { status = "eq:new" }, updates = new { Status = "cancelled" } });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

---

### 11.8 PermScope.Own — Row-Level Security

Sorun: Row-level security authorization katmanına aittir — repository bunu bilmemeli. `EfRepo.List` tenant filtresini uygular çünkü bu veri izolasyonudur (mutlak kural). Own scope ise koşullu yetkilendirmedir — admin tümünü görür, normal kullanıcı sadece kendini. Bu mantık hook'a aittir.

Çözüm: `ICrudHooks<T>`'ye `ApplyScope` metodu eklenir. Default implementasyonu query'yi olduğu gibi döndürür. Kullanıcı override ederek kendi filtresini uygular.

#### ICrudHooks<T> — ApplyScope

```csharp
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
    /// Default: query olduğu gibi döner (filtre yok).
    /// </summary>
    IQueryable<T> ApplyScope(IQueryable<T> query, AppContext ctx) => query;
}
```

#### EfRepo.List — ApplyScope Entegrasyonu

```csharp
public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
{
    var query = _db.Set<T>().AsNoTracking();

    // Hook'tan scope filtresi — yetkilendirme mantığı hook'ta
    query = _hooks.ApplyScope(query, _appContext);

    return await _queryBuilder.Apply(query, listParams, ct);
}

public async Task<T> FindById(string id, CancellationToken ct = default)
{
    var query = _db.Set<T>().AsNoTracking();
    query = _hooks.ApplyScope(query, _appContext);

    return await query.FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw AppError.NotFound();
}
```

#### Kullanım

```csharp
// PermScope.Own — sadece kendi kayıtlarını gör
public class InvoiceHooks : ICrudHooks<Invoice>
{
    public IQueryable<Invoice> ApplyScope(IQueryable<Invoice> query, AppContext ctx)
    {
        if (ctx.CurrentUser.HasPermission("invoices", "read", PermScope.All))
            return query;  // admin → hepsi

        return query.Where(i => i.CreatedBy == ctx.CurrentUser.Id);  // normal → sadece kendi
    }
}

// Department scope
public class ReportHooks : ICrudHooks<Report>
{
    public IQueryable<Report> ApplyScope(IQueryable<Report> query, AppContext ctx)
        => ctx.CurrentUser.HasRole("admin")
            ? query
            : query.Where(r => r.DepartmentId == ctx.CurrentUser.DepartmentId);
}

// Scope gerekmiyorsa — override etme, default boş döner
public class ProductHooks : ICrudHooks<Product> { }
```

#### Testler

```csharp
// ---- ApplyScopeTests.cs (CrudKit.EntityFrameworkCore.Tests) ----
public class ApplyScopeTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task List_ShouldApplyScope_FromHook()
    {
        // user-1 ve user-2 için kayıtlar oluştur
        // InvoiceHooks.ApplyScope → CreatedBy == currentUser.Id filtresi
        // List → sadece user-1 kayıtları dönmeli
    }

    [Fact]
    public async Task FindById_ShouldApplyScope_FromHook()
    {
        // user-2'nin kaydını user-1 ile getirmeye çalış
        // ApplyScope filtresi → NotFound dönmeli
    }

    [Fact]
    public async Task List_ShouldReturnAll_WhenNoScopeApplied()
    {
        // Hook yok veya ApplyScope override edilmemiş → tüm kayıtlar
    }
}
```

---

### 11.9 Soft Delete Restore + Unique Constraint Çakışması

Sorun: Soft-delete edilmiş kayıt restore edilmek istendiğinde aynı unique alana sahip aktif bir kayıt varsa DB constraint ihlali oluşur. `Restore` sadece `DeletedAt = null` yapıyor, çakışma kontrolü yok.

Çözüm: `EfRepo.Restore` içinde `[Unique]` attribute'u olan alanlar için aktif kayıtlarda çakışma kontrolü yapılır. Çakışma varsa `AppError.Conflict` fırlatılır. `ICrudHooks<T>`'ye `BeforeRestore` / `AfterRestore` eklenir.

#### ICrudHooks<T> Güncellemesi

```csharp
public interface ICrudHooks<T> where T : class, IEntity
{
    Task BeforeCreate(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterCreate(T entity, AppContext ctx) => Task.CompletedTask;
    Task BeforeUpdate(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterUpdate(T entity, AppContext ctx) => Task.CompletedTask;
    Task BeforeDelete(T entity, AppContext ctx) => Task.CompletedTask;
    Task AfterDelete(T entity, AppContext ctx) => Task.CompletedTask;
    Task BeforeRestore(T entity, AppContext ctx) => Task.CompletedTask;  // ← yeni
    Task AfterRestore(T entity, AppContext ctx) => Task.CompletedTask;   // ← yeni
}
```

#### EfRepo.Restore Güncellemesi

```csharp
public async Task Restore(string id, CancellationToken ct = default)
{
    // 1. Silinmiş kaydı bul (global filter bypass)
    var entity = await _db.Set<T>()
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(e => e.Id == id, ct)
        ?? throw AppError.NotFound();

    if (entity is not ISoftDeletable softDeletable)
        throw AppError.BadRequest("Bu entity soft-delete desteklemiyor.");

    if (softDeletable.DeletedAt == null)
        throw AppError.BadRequest("Kayıt zaten aktif.");

    // 2. [Unique] alanlarda çakışma kontrolü
    var uniqueProps = typeof(T).GetProperties()
        .Where(p => p.GetCustomAttribute<UniqueAttribute>() != null);

    foreach (var prop in uniqueProps)
    {
        var value = prop.GetValue(entity);
        if (value == null) continue;

        var param = Expression.Parameter(typeof(T), "e");
        var propExpr = Expression.Property(param, prop.Name);
        var valueExpr = Expression.Constant(value, prop.PropertyType);
        var eq = Expression.Equal(propExpr, valueExpr);
        var notDeleted = Expression.Equal(
            Expression.Property(param, nameof(ISoftDeletable.DeletedAt)),
            Expression.Constant(null, typeof(DateTime?)));
        var combined = Expression.AndAlso(eq, notDeleted);
        var lambda = Expression.Lambda<Func<T, bool>>(combined, param);

        var conflict = await _db.Set<T>()
            .IgnoreQueryFilters()
            .AnyAsync(lambda, ct);

        if (conflict)
            throw AppError.Conflict(
                $"'{prop.Name}' alanındaki değer başka bir aktif kayıtta kullanılıyor. " +
                $"Restore işlemi tamamlanamadı.");
    }

    // 3. BeforeRestore hook
    await _hooks.BeforeRestore(entity, _appContext);

    // 4. Restore
    softDeletable.DeletedAt = null;
    entity.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
    await _db.SaveChangesAsync(ct);

    // 5. AfterRestore hook
    await _hooks.AfterRestore(entity, _appContext);
}
```

#### Testler

```csharp
// ---- RestoreUniqueConflictTests.cs (CrudKit.EntityFrameworkCore.Tests) ----
public class RestoreUniqueConflictTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Restore_ShouldSucceed_WhenNoConflict()
    {
        var user = await _f.Repo.Create(new CreateTestUser("ali", "ali@test.com"));
        await _f.Repo.Delete(user.Id);

        var restored = await _f.Repo.Restore(user.Id);
        Assert.Null(((ISoftDeletable)restored).DeletedAt);
    }

    [Fact]
    public async Task Restore_ShouldThrowConflict_WhenUniqueFieldTaken()
    {
        var deleted = await _f.Repo.Create(new CreateTestUser("ali", "ali@test.com"));
        await _f.Repo.Delete(deleted.Id);

        // Aynı email ile yeni user oluştur
        await _f.Repo.Create(new CreateTestUser("ali2", "ali@test.com"));

        var ex = await Assert.ThrowsAsync<AppError>(() => _f.Repo.Restore(deleted.Id));
        Assert.Equal(409, ex.StatusCode);
    }

    [Fact]
    public async Task Restore_ShouldCallBeforeAndAfterHooks()
    {
        // BeforeRestore ve AfterRestore hook'larının çağrıldığını doğrula
    }
}
```

---

### 11.10 IEventBus Delivery Sırası Garantisi

CrudKit event delivery garantisi sağlamaz — bu kullanıcının seçtiği implementasyonun (MassTransit outbox, MediatR, custom) sorumluluğundadır.

Framework sadece şunu garantiler: **Publish her zaman SaveChanges başarıyla tamamlandıktan sonra çağrılır.** SaveChanges başarısızsa Publish çağrılmaz.

```csharp
// EfRepo.Create — garanti edilen sıra
await _db.SaveChangesAsync(ct);                                // önce DB
await _eventBus.Publish(new EntityCreatedEvent(entity));       // sonra event
```

At-least-once delivery için MassTransit Outbox Pattern önerilir:
```csharp
// MassTransit + EF Core Outbox — kullanıcı tarafında
services.AddMassTransit(x =>
{
    x.AddEntityFrameworkOutbox<AppDbContext>(o =>
    {
        o.UsePostgres();
        o.UseBusOutbox();
    });
});
// IEventBus implementasyonu MassTransit publish'e delege edilir
// SaveChanges ile aynı transaction'da outbox tablosuna yazılır
```

Eğer `IEventBus` DI'a kayıtlı değilse CrudKit event publish etmez — hata fırlatmaz.

---

### 11.11 Filter Execution Sırası

`MapCrudEndpoints` tüm filter'ları sabit bir sırayla ekler. Kullanıcı sırayı değiştiremez — sadece hangi filter'ların aktif olduğunu belirtir.

#### Garanti Edilen Sıra

```
1. RequireAuthFilter          → kim olduğunu bilmeden devam etme
2. RequireRoleFilter          → rol kontrolü
3. RequirePermissionFilter    → izin kontrolü
4. WorkflowProtectionFilter   → workflow korumalı alanlar
5. IdempotencyFilter          → auth geçtikten sonra cache kontrol et
6. ValidationFilter           → body doğrulama
7. Handler
```

#### MapCrudEndpoints Güncellemesi

```csharp
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

    group.MapGet("/", ListHandler<TEntity>);
    group.MapGet("/{id}", GetHandler<TEntity>);

    var post = group.MapPost("/", CreateHandler<TEntity, TCreate>);
    ApplyFilters(post, options, typeof(TCreate));

    var put = group.MapPut("/{id}", UpdateHandler<TEntity, TUpdate>);
    ApplyFilters(put, options, typeof(TUpdate));

    var delete = group.MapDelete("/{id}", DeleteHandler<TEntity>);
    ApplyFilters(delete, options, null);

    return group;
}

private static void ApplyFilters(
    RouteHandlerBuilder builder,
    CrudEndpointOptions options,
    Type? dtoType)
{
    // Sıra burada sabitlenir — kullanıcı değiştiremez
    if (options.RequireAuth)
        builder.AddEndpointFilter<RequireAuthFilter>();

    if (options.RequiredRole != null)
        builder.AddEndpointFilter(new RequireRoleFilter(options.RequiredRole));

    if (options.RequiredPermission != null)
        builder.AddEndpointFilter(
            new RequirePermissionFilter(
                options.RequiredPermission.Entity,
                options.RequiredPermission.Action));

    if (options.WorkflowProtection)
        builder.AddEndpointFilter<WorkflowProtectionFilter>();

    if (options.Idempotency)
        builder.AddEndpointFilter<IdempotencyFilter>();

    if (dtoType != null)
        builder.AddEndpointFilter(
            typeof(ValidationFilter<>).MakeGenericType(dtoType));
}
```

#### Idempotency Key — User Prefix

```csharp
// IdempotencyFilter içinde:
var rawKey = ctx.HttpContext.Request.Headers["Idempotency-Key"].ToString();
if (string.IsNullOrEmpty(rawKey))
    return await next(ctx);

// Auth geçtikten sonra geldiği için currentUser güvenli
var currentUser = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
var cacheKey = $"{currentUser.Id ?? "anon"}:{rawKey}";
```

#### Kullanım

```csharp
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders", opts =>
{
    opts.RequireAuth = true;
    opts.RequiredRole = "sales";
    opts.Idempotency = true;
    opts.WorkflowProtection = true;
    // Sıra otomatik: Auth → Role → WorkflowProtection → Idempotency → Validation → Handler
});
```

#### Testler

```csharp
// ---- FilterOrderTests.cs (CrudKit.Api.Tests) ----
public class FilterOrderTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task IdempotencyFilter_ShouldNotBypassAuth()
    {
        // Aynı idempotency key ile önce auth'lu, sonra auth'suz istek
        // Auth'suz istek → 401 (idempotency cache'den dönmemeli)
    }

    [Fact]
    public async Task IdempotencyKey_ShouldBeUserScoped()
    {
        // user-1: idempotency-key: abc → 201 Created
        // user-2: idempotency-key: abc → yeni istek, cache kullanılmaz
    }
}
```

---

### 11.12 Cursor Pagination — Eklenmedi

**Karar:** Eklenmez. Offset pagination büyük dataset'lerde yavaşlayabilir ancak `ISoftDeletable` + `IMultiTenant` filtreleri kayıt sayısını pratikte sınırlar. Gerçekten 1M+ kayıt sorgulayan entity'ler için escape hatch kullanılır. Cursor pagination farklı sort alanlarında karmaşıklaşır ve iki farklı response formatı frontend yükü oluşturur.

---

### 11.13 Sparse Fieldsets — Eklenmedi

**Karar:** Eklenmez. `[SkipResponse]` ile hassas alanlar zaten çıkarılıyor. Dynamic projection EF Core'da tip güvenliğini zorlaştırır. Bant genişliği optimizasyonu framework'ün sorumluluğu değil.

---

### 11.14 Cross-Tenant Create Koruması

Sorun: `IMultiTenant` entity için DTO'da `TenantId` alanı varsa kullanıcı başkasının tenant'ına kayıt atabilir.

Çözüm: `EfRepo.Create` ve `EfRepo.Update` sırasında `IMultiTenant` entity'lerde `TenantId` property'si DTO'dan map edilmez. `CrudKitDbContext.BeforeSaveChanges` zaten `ICurrentUser.TenantId` ile override eder; bu ikinci savunma hattıdır.

#### EfRepo.Create — TenantId Koruması

```csharp
public async Task<T> Create(object createDto, CancellationToken ct = default)
{
    var entity = Activator.CreateInstance<T>();

    foreach (var prop in createDto.GetType().GetProperties())
    {
        var value = prop.GetValue(createDto);

        // IMultiTenant entity'lerde TenantId DTO'dan map edilmez
        if (typeof(IMultiTenant).IsAssignableFrom(typeof(T))
            && prop.Name == nameof(IMultiTenant.TenantId))
            continue;

        // [Protected] alanlar map edilmez
        var entityProp = typeof(T).GetProperty(prop.Name);
        if (entityProp?.GetCustomAttribute<ProtectedAttribute>() != null)
            continue;

        MapProperty(entity, entityProp, value);
    }

    // TenantId CrudKitDbContext.BeforeSaveChanges'ta ICurrentUser'dan set edilir
    _db.Set<T>().Add(entity);
    await _db.SaveChangesAsync(ct);

    return entity;
}
```

#### Testler

```csharp
// ---- CrossTenantProtectionTests.cs (CrudKit.EntityFrameworkCore.Tests) ----
public class CrossTenantProtectionTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task Create_ShouldIgnoreTenantIdFromDto()
    {
        // ICurrentUser.TenantId = "tenant-1"
        // DTO'da TenantId = "tenant-evil" gönderildi
        // Kaydın TenantId'si "tenant-1" olmalı
        var dto = new { Name = "Test", TenantId = "tenant-evil" };
        var entity = await _f.Repo.Create(dto);
        Assert.Equal("tenant-1", ((IMultiTenant)entity).TenantId);
    }

    [Fact]
    public async Task Update_ShouldIgnoreTenantIdFromDto()
    {
        var entity = await _f.Repo.Create(new { Name = "Test" });
        var updated = await _f.Repo.Update(entity.Id, new { TenantId = "tenant-evil" });
        Assert.Equal("tenant-1", ((IMultiTenant)updated).TenantId);
    }
}
```

---

### 11.15 Bulk Operation Limiti

Sorun: Filtresiz veya geniş filtreli bulk işlem yüz binlerce kaydı etkiler, timeout veya performans sorununa yol açar.

Çözüm: Limit iki seviyede configure edilir — global default `CrudKitApiOptions`'da, entity bazında `CrudEntityAttribute` ile override edilir. Öncelik: entity attribute > global options > varsayılan (10.000). Count-first akışı convention olarak belgelenir.

#### CrudKitApiOptions — Global Default

```csharp
public class CrudKitApiOptions
{
    // ... mevcut alanlar ...
    public int BulkLimit { get; set; } = 10_000;  // tüm entity'ler için global default
}
```

#### CrudEntityAttribute — Entity Bazında Override

```csharp
public class CrudEntityAttribute : Attribute
{
    // ... mevcut alanlar ...
    public bool EnableBulkUpdate { get; set; } = false;
    public bool EnableBulkDelete { get; set; } = false;

    /// <summary>
    /// Bulk işlem limiti. 0 ise global CrudKitApiOptions.BulkLimit kullanılır.
    /// </summary>
    public int BulkLimit { get; set; } = 0;
}
```

#### EfRepo.BulkUpdate — Limit Kontrolü

```csharp
public async Task<int> BulkUpdate(
    Dictionary<string, FilterOp> filters,
    Dictionary<string, object?> updates,
    CancellationToken ct = default)
{
    var attr = typeof(T).GetCustomAttribute<CrudEntityAttribute>();
    if (attr?.EnableBulkUpdate != true)
        throw AppError.BadRequest("Bu entity için bulk update kapalı.");

    // Öncelik: entity attribute > global options > varsayılan
    var limit = attr.BulkLimit > 0
        ? attr.BulkLimit
        : _options.BulkLimit;

    var query = _db.Set<T>().AsQueryable();
    query = _filterApplier.ApplyAll(query, filters);

    var count = await query.LongCountAsync(ct);
    if (count > limit)
        throw AppError.BadRequest(
            $"İşlem {count} kaydı etkiliyor, limit {limit}. " +
            $"Önce /bulk/count ile kontrol edin, filtreyi daraltın.");

    return await query.ExecuteUpdateAsync(BuildUpdateSetters(updates), ct);
}
```

#### Kullanım

```csharp
// Global limit — tüm bulk entity'ler için
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.BulkLimit = 50_000;
});

// Entity bazında override
[CrudEntity(Table = "products", EnableBulkUpdate = true, BulkLimit = 100_000)]
public class Product : IEntity { ... }

[CrudEntity(Table = "invoices", EnableBulkUpdate = true, BulkLimit = 500)]
public class Invoice : IEntity { ... }

[CrudEntity(Table = "logs", EnableBulkDelete = true)]  // BulkLimit = 0 → global 50.000 kullanılır
public class SystemLog : IEntity { ... }
```

#### Convention: Count-First Akışı

```
1. POST /api/products/bulk/count  → { "count": 8500 }
2. Kullanıcı onaylar
3. PUT  /api/products/bulk        → { "affected": 8500 }
```

#### Testler

```csharp
// ---- BulkLimitTests.cs (CrudKit.Api.Tests) ----
public class BulkLimitTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task BulkUpdate_ShouldReturn400_WhenEntityLimitExceeded()
    {
        // Entity BulkLimit = 100, 150 kayıt var → 400
        var response = await _client.PutAsJsonAsync("/api/limited-products/bulk",
            new { filters = new { }, updates = new { IsActive = false } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkUpdate_ShouldUseGlobalLimit_WhenEntityLimitNotSet()
    {
        // Entity BulkLimit = 0, global = 50.000, 60.000 kayıt → 400
    }

    [Fact]
    public async Task BulkUpdate_ShouldSucceed_WhenWithinLimit()
    {
        // 50 kayıt, limit 100 → başarılı
    }

    [Fact]
    public async Task EntityLimit_ShouldOverride_GlobalLimit()
    {
        // Global = 50.000, entity = 500, 1.000 kayıt → 400 (entity limiti geçerli)
    }
}
```

---

### 11.16 Concurrency

**ETag / If-Match eklenmedi.** `IConcurrent` + `DbUpdateConcurrencyException → 409` yeterlidir. HTTP ETag protokolü framework scope'u dışında — isteyenler middleware olarak ekleyebilir.

#### IConcurrent + RowVersion (EF Core Seviyesi)

```csharp
// IConcurrent implemente eden entity'lerde RowVersion otomatik aktif
// CrudKitDbContext.OnModelCreating'de:
modelBuilder.Entity(clrType).Property(nameof(IConcurrent.RowVersion)).IsRowVersion();
```

#### DbUpdateConcurrencyException → 409

`AppErrorFilter` çakışmayı yakalar, client bilgilendirilir:

```csharp
catch (DbUpdateConcurrencyException)
{
    return Results.Problem(
        statusCode: 409,
        title: "Conflict",
        detail: "Kayıt başkası tarafından değiştirildi. Güncel versiyonu almak için tekrar GET yapın.");
}
```

#### IConcurrent Olmayan Entity'lerde Son-Yazan-Kazanır

`IConcurrent` implemente etmeyen entity'lerde concurrency koruması yoktur. İki eş zamanlı PUT gelirse sonuncusu geçerli olur — **kasıtlı davranış**, dokümanda açıkça belirtilir.

```
IConcurrent var    → RowVersion koruması aktif, çakışmada 409
IConcurrent yok    → Son-yazan-kazanır, uygulama bunu kabul etmiştir
```

#### Bulk Update + Concurrency Çelişkisi

`ExecuteUpdate` EF Core change tracker'ı bypass eder — `RowVersion` güncellenmez. `IConcurrent` entity'lerde `EnableBulkUpdate = true` kullanımı **önerilmez**. Startup'ta WARNING:

```csharp
if (typeof(IConcurrent).IsAssignableFrom(typeof(T)))
{
    var attr = typeof(T).GetCustomAttribute<CrudEntityAttribute>();
    if (attr?.EnableBulkUpdate == true)
        logger.LogWarning(
            "{Entity} hem IConcurrent hem EnableBulkUpdate kullanıyor. " +
            "Bulk update RowVersion'ı güncellemez — concurrency koruması devre dışı kalır.",
            typeof(T).Name);
}
```

#### Testler

```csharp
// ---- ConcurrencyTests.cs (CrudKit.Api.Tests) ----
public class ConcurrencyTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ConcurrentUpdate_ShouldReturn409_ForSecondRequest()
    {
        // Aynı kayda eş zamanlı iki PUT → ikincisi 409 almalı
    }

    [Fact]
    public async Task Update_ShouldSucceed_WhenNoConflict()
    {
        // Sıralı iki PUT → her ikisi de 200 almalı
    }

    [Fact]
    public async Task NonConcurrentEntity_ShouldAllowLastWriteWins()
    {
        // IConcurrent yok → eş zamanlı PUT'lar her ikisi de 200 alır, sonuncusu geçerli
    }
}
```

---

### 11.17 TimeProvider — Test Zaman İzolasyonu

Sorun: `CrudKitDbContext.BeforeSaveChanges` içinde `DateTime.UtcNow` doğrudan kullanılıyor. Zaman bağımlı testler flaky, zaman atlama testi imkansız.

Çözüm: .NET 8+ built-in `TimeProvider` abstract class. `CrudKitDbContext` constructor'dan inject alır.

#### CrudKitDbContext Güncellemesi

```csharp
public abstract class CrudKitDbContext : DbContext
{
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    protected CrudKitDbContext(
        DbContextOptions options,
        ICurrentUser currentUser,
        TimeProvider? timeProvider = null) : base(options)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private void BeforeSaveChanges()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;  // DateTime.UtcNow yerine

        foreach (var entry in ChangeTracker.Entries<IEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    if (entry.Entity is IMultiTenant t && _currentUser.TenantId != null)
                        t.TenantId = _currentUser.TenantId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(nameof(IEntity.CreatedAt)).IsModified = false;
                    break;
                case EntityState.Deleted:
                    if (entry.Entity is ISoftDeletable sd)
                    {
                        entry.State = EntityState.Modified;
                        sd.DeletedAt = now;
                    }
                    break;
            }
        }

        WriteAuditLogs();
    }
}
```

#### DI Kaydı

```csharp
// Production — TimeProvider.System varsayılan, kayıt gerekmez
builder.Services.AddCrudKit<AppDbContext>();

// Test
services.AddSingleton<TimeProvider>(
    new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
```

#### Testler

```csharp
// ---- TimeProviderTests.cs (CrudKit.EntityFrameworkCore.Tests) ----
public class TimeProviderTests
{
    [Fact]
    public async Task Create_ShouldUseTimeProvider_ForTimestamps()
    {
        var fakeTime = new FakeTimeProvider();
        var fixedTime = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(fixedTime);

        var entity = await _repo.Create(new CreateTestProduct("Test", "TP-001", 100));

        Assert.Equal(fixedTime.UtcDateTime, entity.CreatedAt);
        Assert.Equal(fixedTime.UtcDateTime, entity.UpdatedAt);
    }

    [Fact]
    public async Task SoftDelete_DeletedAt_ShouldBeAfterCreatedAt()
    {
        var fakeTime = new FakeTimeProvider();
        fakeTime.SetUtcNow(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        var entity = await _repo.Create(new CreateTestProduct("Test", "TP-002", 100));

        fakeTime.Advance(TimeSpan.FromHours(1));
        await _repo.Delete(entity.Id);

        var deleted = await _db.Set<TestProduct>()
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == entity.Id);

        Assert.True(deleted.DeletedAt > deleted.CreatedAt);
    }
}
```

---

### 11.18 Navigation Property Circular Reference

Sorun: `Order → List<OrderItem>` ve `OrderItem → Order` back-reference varsa JSON serialization sonsuz döngüye girer.

Çözüm: `IgnoreCycles` global olarak aktif edilir — döngü tespit edilince ilgili referans `null` yazılır, crash olmaz. Navigation property'lerin response şeklini yönetmek kullanıcının sorumluluğundadır: `IEntityMapper` kullanıyorsa zaten sadece istediği alanları döndürür, kullanmıyorsa `[SkipResponse]` ile back-reference'ları çıkarabilir.

#### JSON Konfigürasyonu (CrudKit.Api)

```csharp
builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});
```

#### Kullanıcı Sorumluluğu

```csharp
// Seçenek 1: IEntityMapper ile (önerilen)
// Mapper sadece istenen alanları döndürür — navigation property zaten yok
public class OrderItemMapper : IEntityMapper<OrderItem, OrderItemResponse>
{
    public OrderItemResponse Map(OrderItem e)
        => new(e.Id, e.OrderId, e.ProductId, e.Quantity, e.UnitPrice);
    // Order back-reference'ı response'a dahil edilmedi
}

// Seçenek 2: [SkipResponse] ile
public class OrderItem : IEntity
{
    public string OrderId { get; set; } = string.Empty;

    [SkipResponse]
    public Order Order { get; set; } = null!;  // back-ref → response'da yok
}
```

#### Testler

```csharp
// ---- CircularReferenceTests.cs (CrudKit.Api.Tests) ----
public class CircularReferenceTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Get_ShouldNotCrash_WhenCircularReferenceExists()
    {
        // Navigation property back-reference var ama IgnoreCycles aktif
        // 500 değil, 200 dönmeli
        var response = await _client.GetAsync("/api/order-items/123");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

---

### 11.19 Workflow Versioning

Sorun: Aktif instance'lar varken yeni workflow versiyonu deploy edilirse instance'lar hangi versiyona göre devam eder?

Çözüm: Instance başladığı versiyona kilitlenir. Yeni versiyon deploy edilince eski `is_active = false` yapılır — yeni instance'lar yeni versiyondan başlar, eski instance'lar kendi versiyonlarında tamamlanır. Eski tanımlar silinmez.

#### workflow_definitions Tablosu

```sql
-- is_active: sadece bir versiyon aktif olabilir
CREATE UNIQUE INDEX idx_workflow_active
  ON workflow_definitions(name)
  WHERE is_active = true;
```

#### WorkflowEngine — Versiyon Yönetimi

```csharp
public class WorkflowEngine
{
    // Yeni instance → aktif versiyona kilitlenir
    public async Task<WorkflowInstance> Start(string workflowName, ...)
    {
        var definition = await _db.WorkflowDefinitions
            .Where(d => d.Name == workflowName && d.IsActive)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"'{workflowName}' için aktif workflow tanımı bulunamadı.");

        var instance = new WorkflowInstance
        {
            WorkflowName = workflowName,
            WorkflowVersion = definition.Version,  // versiyona kilitlenir
            // ...
        };
        // ...
    }

    // Mevcut instance kendi versiyonuna göre çalışır (is_active değil, version'a göre)
    private async Task<WorkflowDefinition> GetDefinitionForInstance(WorkflowInstance instance)
        => await _db.WorkflowDefinitions
            .Where(d => d.Name == instance.WorkflowName
                     && d.Version == instance.WorkflowVersion)
            .FirstOrDefaultAsync()
            ?? throw new InvalidOperationException(
                $"Workflow tanımı bulunamadı: {instance.WorkflowName} v{instance.WorkflowVersion}");

    // Yeni versiyon deploy et
    public async Task DeployVersion(WorkflowDefinition newDefinition)
    {
        var current = await _db.WorkflowDefinitions
            .Where(d => d.Name == newDefinition.Name && d.IsActive)
            .FirstOrDefaultAsync();

        if (current != null)
            current.IsActive = false;  // eskiyi dondur

        newDefinition.IsActive = true;
        newDefinition.Version = (current?.Version ?? 0) + 1;
        _db.WorkflowDefinitions.Add(newDefinition);

        await _db.SaveChangesAsync();
    }
}
```

#### Sonuç

```
Deployment sonrası:
  purchase_approval v1 → is_active = false  (50 aktif instance devam ediyor)
  purchase_approval v2 → is_active = true   (yeni instance'lar buradan başlar)

✅ Mevcut instance'lar etkilenmez
✅ Deployment bloke olmaz
✅ v1 tanımı arşivde kalır
✅ Yeni instance'lar v2'den başlar
```

#### Testler

```csharp
// ---- WorkflowVersioningTests.cs (CrudKit.Workflow.Tests) ----
public class WorkflowVersioningTests : IClassFixture<WorkflowFixture>
{
    [Fact]
    public async Task ActiveInstance_ShouldContinueOnOriginalVersion()
    {
        var instance = await _engine.Start("purchase_approval", ...);
        Assert.Equal(1, instance.WorkflowVersion);

        await _engine.DeployVersion(new WorkflowDefinition { Name = "purchase_approval" });

        var current = await _engine.GetInstance(instance.Id);
        Assert.Equal(1, current.WorkflowVersion);  // v1'de devam ediyor
    }

    [Fact]
    public async Task NewInstance_ShouldStartOnLatestVersion()
    {
        // v2 aktif
        var instance = await _engine.Start("purchase_approval", ...);
        Assert.Equal(2, instance.WorkflowVersion);
    }

    [Fact]
    public async Task Deploy_ShouldDeactivatePreviousVersion()
    {
        await _engine.DeployVersion(new WorkflowDefinition { Name = "purchase_approval" });

        var definitions = await _db.WorkflowDefinitions
            .Where(d => d.Name == "purchase_approval").ToListAsync();

        Assert.Equal(1, definitions.Count(d => d.IsActive));
        Assert.Equal(definitions.Max(d => d.Version),
            definitions.Single(d => d.IsActive).Version);
    }
}
```

---

### 11.20 ResponseDto — IEntityMapper

Sorun: Entity doğrudan serialize edildiğinde response şekli entity şekline bağımlıdır. Hesaplanmış alanlar, rename, birleştirme gibi ihtiyaçlar `[SkipResponse]` ile karşılanamaz.

Çözüm: `IEntityMapper<TEntity, TResponse>` interface'i CrudKit.Core'da tanımlıdır. Kullanıcı implemente eder ve DI'a kaydeder. Kayıtlı değilse entity doğrudan döner — mevcut davranış korunur.

```csharp
// ---- CrudKit.Core/Interfaces/IEntityMapper.cs ----

/// <summary>
/// Entity'yi response DTO'ya dönüştürür.
/// DI'a kayıtlı değilse entity doğrudan serialize edilir.
/// </summary>
public interface IEntityMapper<TEntity, TResponse>
    where TEntity : class, IEntity
{
    TResponse Map(TEntity entity);
}
```

#### Handler'larda Kullanım

```csharp
// GetHandler, ListHandler, CreateHandler, UpdateHandler içinde:
static async Task<IResult> GetHandler<TEntity>(
    string id,
    IRepo<TEntity> repo,
    IServiceProvider services,
    CancellationToken ct) where TEntity : class, IEntity
{
    var entity = await repo.FindByIdOrDefault(id, ct);
    if (entity is null) return Results.NotFound();

    // IEntityMapper kayıtlıysa map et, yoksa entity'yi direkt döndür
    var mapper = services.GetService<IEntityMapper<TEntity, object>>();
    return mapper != null
        ? Results.Ok(mapper.Map(entity))
        : Results.Ok(entity);
}
```

#### Kullanıcı Tarafı

```csharp
// Response DTO
public record UserResponse(
    string Id,
    string Username,
    string Email,
    string FullName,
    bool IsActive
);

// Mapper
public class UserMapper : IEntityMapper<User, UserResponse>
{
    public UserResponse Map(User entity) => new(
        entity.Id,
        entity.Username,
        entity.Email,
        $"{entity.FirstName} {entity.LastName}",  // hesaplanmış alan
        entity.IsActive
    );
}

// Program.cs
builder.Services.AddScoped<IEntityMapper<User, UserResponse>, UserMapper>();
```

#### Mapper Kayıtlı Değilse

```csharp
// Mapper yok → entity doğrudan döner
// [SkipResponse] attribute'ları hâlâ uygulanır
// Mevcut davranış korunur
```

#### SourceGen Entegrasyonu

`CrudKit.SourceGen` paketi eklendiğinde `[CrudEntity]` attribute'u olan her entity için `XResponse` record'ı ve `XMapper` class'ı otomatik üretilir (`[SkipResponse]` alanlar hariç). Kullanıcı özelleştirme gerekiyorsa partial class ile genişletir.

#### Testler

```csharp
// ---- EntityMapperTests.cs (CrudKit.Api.Tests) ----
public class EntityMapperTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Get_ShouldUseMapper_WhenRegistered()
    {
        // UserMapper kayıtlı → UserResponse döner
        var response = await _client.GetAsync("/api/users/123");
        var obj = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(obj.TryGetProperty("fullName", out _));   // mapper'dan gelen alan
        Assert.False(obj.TryGetProperty("firstName", out _)); // entity'de var ama mapper'da yok
    }

    [Fact]
    public async Task Get_ShouldReturnEntity_WhenMapperNotRegistered()
    {
        // ProductMapper yok → entity doğrudan döner
        var response = await _client.GetAsync("/api/products/123");
        var obj = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.True(obj.TryGetProperty("sku", out _));  // entity alanı
    }
}
```

---

### 11.21 Error Handling

#### 11.21.1 Unhandled Exception → 500

`AppErrorFilter` tüm exception'ları yakalar. `AppError` uygun status code ile döner, diğerleri her zaman 500'e dönüştürülür. Production'da stack trace asla expose edilmez.

```csharp
// ---- AppErrorFilter.cs (CrudKit.Api) ----
public class AppErrorFilter : IEndpointFilter
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<AppErrorFilter> _logger;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (AppError ex)
        {
            // CrudKit exception'ları — uygun status code
            return ex.StatusCode switch
            {
                400 when ex.Details is ValidationErrors ve =>
                    Results.ValidationProblem(ve.ToDictionary()),
                _ => Results.Problem(
                    statusCode: ex.StatusCode,
                    title: ex.Message,
                    detail: ex.Details?.ToString())
            };
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(
                statusCode: 409,
                title: "Conflict",
                detail: "Kayıt başkası tarafından değiştirildi. Güncel versiyonu almak için tekrar GET yapın.");
        }
        catch (Exception ex)
        {
            // Beklenmeyen exception — her zaman 500
            _logger.LogError(ex, "Beklenmeyen hata: {Message}", ex.Message);

            var detail = _env.IsDevelopment()
                ? ex.ToString()           // Development: stack trace dahil
                : "Beklenmeyen bir hata oluştu";  // Production: generic mesaj

            return Results.Problem(statusCode: 500, detail: detail);
        }
    }
}
```

#### 11.21.2 Validation Formatı

`IValidator<T>` DI'da kayıtlıysa FluentValidation çalışır, yoksa DataAnnotation kullanılır. Her iki kaynak da `ValidationErrors` modeline normalize edilir.

```csharp
// ---- ValidationFilter.cs ----
public class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var dto = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (dto is null) return await next(ctx);

        var errors = new ValidationErrors();

        // FluentValidation varsa — DataAnnotation atlanır
        var fluentValidator = ctx.HttpContext.RequestServices.GetService<IValidator<T>>();
        if (fluentValidator != null)
        {
            var result = await fluentValidator.ValidateAsync(dto);
            foreach (var failure in result.Errors)
                errors.Add(failure.PropertyName, failure.ErrorCode, failure.ErrorMessage);
        }
        else
        {
            // DataAnnotation fallback
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(dto, new ValidationContext(dto), validationResults, true))
                foreach (var vr in validationResults)
                    errors.Add(
                        vr.MemberNames.FirstOrDefault() ?? "",
                        "Validation",
                        vr.ErrorMessage ?? "Geçersiz değer");
        }

        if (!errors.IsEmpty)
            return Results.ValidationProblem(errors.ToDictionary());

        return await next(ctx);
    }
}
```

#### Testler

```csharp
// ---- ErrorHandlingTests.cs (CrudKit.Api.Tests) ----
public class ErrorHandlingTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UnhandledException_ShouldReturn500_WithGenericMessage_InProduction()
    {
        // Hook'tan NullReferenceException fırlatılıyor
        // Production env → generic mesaj, stack trace yok
        var response = await _client.PostAsJsonAsync("/api/crash-test", new { });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StackTrace", body);
        Assert.Contains("Beklenmeyen bir hata oluştu", body);
    }

    [Fact]
    public async Task UnhandledException_ShouldReturn500_WithStackTrace_InDevelopment()
    {
        // Development env → stack trace dahil
    }

    [Fact]
    public async Task FluentValidation_ShouldTakePrecedence_OverDataAnnotation()
    {
        // IValidator<T> kayıtlı → FluentValidation çalışır
        // DataAnnotation hataları dönmez
    }

    [Fact]
    public async Task DataAnnotation_ShouldWork_WhenNoFluentValidator()
    {
        // IValidator<T> yok → DataAnnotation hataları dönmeli
        var response = await _client.PostAsJsonAsync("/api/products",
            new { Name = "", Price = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

---

### 11.22 OpenAPI / Swagger Metadata

CrudKit hangi UI kullanılacağına karışmaz. Sorumluluk: `MapCrudEndpoints` tarafından üretilen her endpoint'e doğru OpenAPI metadata'sı eklenir.

#### MapCrudEndpoints — Otomatik Metadata

```csharp
group.MapGet("/", ListHandler<TEntity>)
     .WithName($"{entityName}_List")
     .WithTags(entityName)
     .Produces<Paginated<TEntity>>(200)
     .ProducesProblem(401)
     .ProducesProblem(403);

group.MapGet("/{id}", GetHandler<TEntity>)
     .WithName($"{entityName}_GetById")
     .WithTags(entityName)
     .Produces<TEntity>(200)
     .ProducesProblem(404);

group.MapPost("/", CreateHandler<TEntity, TCreate>)
     .WithName($"{entityName}_Create")
     .WithTags(entityName)
     .Accepts<TCreate>("application/json")
     .Produces<TEntity>(201)
     .ProducesValidationProblem()
     .ProducesProblem(401)
     .ProducesProblem(409);

group.MapPut("/{id}", UpdateHandler<TEntity, TUpdate>)
     .WithName($"{entityName}_Update")
     .WithTags(entityName)
     .Accepts<TUpdate>("application/json")
     .Produces<TEntity>(200)
     .ProducesValidationProblem()
     .ProducesProblem(404)
     .ProducesProblem(412)  // IConcurrent entity'ler için
     .ProducesProblem(428);

group.MapDelete("/{id}", DeleteHandler<TEntity>)
     .WithName($"{entityName}_Delete")
     .WithTags(entityName)
     .Produces(204)
     .ProducesProblem(404);
```

#### Kullanıcı Tarafı — UI Seçimi

```csharp
// .NET 9 built-in + Scalar (önerilen)
builder.Services.AddOpenApi();
app.MapOpenApi();
app.MapScalarApiReference();

// veya Swashbuckle
builder.Services.AddSwaggerGen();
app.UseSwagger();
app.UseSwaggerUI();

// CrudKit her ikisiyle de çalışır — UI seçimi kullanıcıya ait
```

---

### 11.23 Logging

`Microsoft.Extensions.Logging` kullanılır. Her bileşen kendi category'si ile loglar. Kullanıcı `appsettings.json`'dan seviyeyi kontrol eder.

#### Log Category'leri

```
CrudKit.Repository   → EfRepo operasyonları
CrudKit.Api          → Endpoint, filter, error handling
CrudKit.Workflow     → WorkflowEngine, step execution
CrudKit.SourceGen    → (build-time, runtime log yok)
```

#### Log Seviyeleri

```csharp
// CrudKit.Repository
_logger.LogDebug("List {Entity} — filtre: {Filters}, süre: {Ms}ms",
    typeof(T).Name, listParams.Filters.Keys, elapsed);

_logger.LogDebug("Create {Entity} id={Id}", typeof(T).Name, entity.Id);

// CrudKit.Api
_logger.LogError(ex, "Beklenmeyen hata: {Method} {Path}",
    ctx.HttpContext.Request.Method, ctx.HttpContext.Request.Path);

_logger.LogWarning("Soft delete restore conflict: {Entity} id={Id}, alan={Field}",
    typeof(T).Name, id, conflictingField);

// CrudKit.Workflow
_logger.LogInformation("Workflow {Name} v{Version} başlatıldı: instance={Id}, entity={EntityId}",
    workflowName, version, instance.Id, entityId);

_logger.LogInformation("Step {Step} tamamlandı: instance={Id}, süre={Ms}ms",
    stepId, instanceId, elapsed);

_logger.LogWarning("Timeout: instance={Id}, step={Step}, action={Action}",
    instanceId, stepId, timeoutAction);

// Startup warnings
_logger.LogWarning("{Entity} için OwnerField bulunamadı. PermScope.Own uygulanmayacak.",
    entityType.Name);

_logger.LogWarning("{Entity} hem IConcurrent hem EnableBulkUpdate kullanıyor.",
    entityType.Name);
```

#### appsettings.json Kontrolü

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "CrudKit": "Warning",
      "CrudKit.Repository": "Debug",
      "CrudKit.Workflow": "Information"
    }
  }
}
```

#### Testler

```csharp
// ---- LoggingTests.cs (CrudKit.Api.Tests) ----
public class LoggingTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Create_ShouldLogDebug_WithEntityNameAndId()
    {
        // ILogger mock ile create işlemi yapılır
        // LogDebug çağrısı entity adı ve id içermeli
    }

    [Fact]
    public async Task UnhandledException_ShouldLogError_WithDetails()
    {
        // Exception fırlatıldığında LogError çağrılmalı
    }

    [Fact]
    public async Task WorkflowStart_ShouldLogInformation()
    {
        // WorkflowEngine.Start → LogInformation çağrılmalı
    }
}
```

---

### 11.24 Startup Validation Mimarisi

Startup validasyonları iki kategoriye ayrılır: DB gerektirmeyen (reflection/metadata) ve DB gerektiren (workflow tanımları). Bunlar farklı yerlerde çalışır.

#### DB Gerektirmeyen → AddCrudKit() İçinde

```csharp
public static IServiceCollection AddCrudKit<TContext>(
    this IServiceCollection services,
    Action<CrudKitApiOptions>? configure = null)
    where TContext : CrudKitDbContext
{
    // ...

    // DB gerektirmeyen validasyonlar — register anında çalışır
    ValidateEntityMetadata(services);

    return services;
}

private static void ValidateEntityMetadata(IServiceCollection services)
{
    // Tüm register edilmiş entity tiplerini tara
    foreach (var entityType in DiscoverEntityTypes())
    {
        var attr = entityType.GetCustomAttribute<CrudEntityAttribute>();
        if (attr == null) continue;

        // OwnerField property'si gerçekten var mı?
        if (!string.IsNullOrEmpty(attr.OwnerField)
            && entityType.GetProperty(attr.OwnerField) == null)
            throw new InvalidOperationException(
                $"[CrudEntity(OwnerField = \"{attr.OwnerField}\")] — " +
                $"{entityType.Name} sınıfında '{attr.OwnerField}' property'si bulunamadı.");

        // IConcurrent + EnableBulkUpdate çelişkisi
        if (typeof(IConcurrent).IsAssignableFrom(entityType)
            && attr.EnableBulkUpdate)
            // Throw değil — Warning. Geçerli kullanım olabilir.
            Console.WriteLine(
                $"[WARNING] {entityType.Name}: IConcurrent + EnableBulkUpdate — " +
                "Bulk update RowVersion'ı güncellemez.");

        // WorkflowProtected alanları entity'de var mı?
        if (attr.WorkflowProtected != null)
        {
            foreach (var field in attr.WorkflowProtected)
            {
                if (entityType.GetProperty(field) == null)
                    throw new InvalidOperationException(
                        $"[CrudEntity(WorkflowProtected)] — " +
                        $"{entityType.Name} sınıfında '{field}' property'si bulunamadı.");
            }
        }
    }
}
```

#### DB Gerektiren → CrudKitStartupValidator : IHostedService

```csharp
public class CrudKitStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CrudKitStartupValidator> _logger;

    public CrudKitStartupValidator(
        IServiceProvider services,
        ILogger<CrudKitStartupValidator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();

        await ValidateWorkflowActions(scope, ct);
        await ValidateWorkflowDefinitions(scope, ct);
    }

    private async Task ValidateWorkflowActions(IServiceScope scope, CancellationToken ct)
    {
        // DB'deki tüm aktif workflow step'lerinin action_key'lerini al
        // ActionRegistry'de kayıtlı mı kontrol et
        // Kayıtlı değilse → uygulama başlamayı durdur
        var registry = scope.ServiceProvider.GetRequiredService<ActionRegistry>();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var actionKeys = await db.WorkflowSteps
            .Where(s => s.Kind == "action" && s.WorkflowDefinition.IsActive)
            .Select(s => s.ActionKey)
            .Distinct()
            .ToListAsync(ct);

        var missing = actionKeys
            .Where(k => !registry.ListActions().Contains(k))
            .ToList();

        if (missing.Any())
            throw new InvalidOperationException(
                $"Workflow action key'leri ActionRegistry'de bulunamadı: " +
                $"{string.Join(", ", missing)}");
    }

    private async Task ValidateWorkflowDefinitions(IServiceScope scope, CancellationToken ct)
    {
        // [CrudEntity(Workflow = "x")] olan entity'lerin workflow'ları DB'de var mı?
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        foreach (var entityType in DiscoverEntityTypes())
        {
            var attr = entityType.GetCustomAttribute<CrudEntityAttribute>();
            if (string.IsNullOrEmpty(attr?.Workflow)) continue;

            var exists = await db.WorkflowDefinitions
                .AnyAsync(d => d.Name == attr.Workflow && d.IsActive, ct);

            if (!exists)
                _logger.LogWarning(
                    "{Entity} için workflow '{Workflow}' DB'de aktif tanım bulunamadı.",
                    entityType.Name, attr.Workflow);
            // Throw değil — warning. Workflow sonradan seed edilebilir.
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

#### DI Kaydı

```csharp
// AddCrudKit içinde otomatik register edilir
services.AddHostedService<CrudKitStartupValidator>();
```

#### Özet

```
AddCrudKit()                  → OwnerField, WorkflowProtected, BulkUpdate çelişkisi
                                 Hata varsa uygulama başlamaz (throw)

CrudKitStartupValidator       → Workflow action key'leri, workflow tanımları
(IHostedService)                 Kritik eksikler throw, uyarılar WARNING log
```

---

### 11.25 IEventBus — Kullanıcı Sorumluluğu

Framework event publish etmez. `EntityCreatedEvent`, `EntityUpdatedEvent`, `EntityDeletedEvent` sadece convention olarak Core'da tanımlıdır — kullanıcı isterse hook'ta kullanır.

```csharp
// ---- Kullanıcı tarafı ----
public class OrderHooks : ICrudHooks<Order>
{
    private readonly IEventBus _eventBus;
    public OrderHooks(IEventBus eventBus) => _eventBus = eventBus;

    public async Task AfterCreate(Order entity, AppContext ctx)
    {
        // Manuel publish — framework yapmaz
        await _eventBus.Publish(new EntityCreatedEvent
        {
            EventId = Guid.NewGuid().ToString(),
            OccurredAt = DateTime.UtcNow,
            EntityType = nameof(Order),
            EntityId = entity.Id
        });
    }
}
```

At-least-once delivery gerekiyorsa MassTransit Outbox Pattern önerilir — bkz. 11.10.

---

### 11.26 IApprovable — Kaldırıldı

**Karar:** `IApprovable` ve `ApprovalStepDefinition` kaldırıldı.

Approval senaryoları workflow engine'in `approval` step kind'ı ile karşılanır. İki farklı approval mekanizması gereksiz karmaşıklık yaratır; workflow engine kim onayladı, ne zaman, hangi comment ile gibi bilgileri zaten yönetir.

```
// Eskisi — IApprovable (kaldırıldı)
public class Invoice : IEntity, IApprovable
{
    public static IReadOnlyList<ApprovalStepDefinition> ApprovalSteps => [...];
}

// Yenisi — Workflow engine approval step
// workflow_steps tablosunda:
// { "kind": "approval", "config": { "role": "finance", "min_approvals": 2 } }
```

---

### 11.27 IModule — Lifecycle ve Kayıt

`IModule` interface'i modular monolith desteği için korunur. Otomatik assembly scan ve manuel kayıt birlikte desteklenir.

#### Lifecycle

```
1. AddCrudKit() / AddCrudKitModule<T>()  → IModule instance DI'a eklenir,
                                            RegisterServices çağrılır
2. app.UseCrudKit()                       → MapEndpoints çağrılır
3. ActionRegistry build edilirken         → RegisterWorkflowActions çağrılır
```

#### Otomatik Scan

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;
    // Assembly'deki tüm IModule implementasyonları otomatik register edilir
});
```

#### Manuel Kayıt

```csharp
// Farklı assembly'den modül ekleme
builder.Services.AddCrudKitModule<ThirdPartyModule>();
```

#### İkisi Birlikte

```csharp
builder.Services.AddCrudKit<AppDbContext>(opts =>
{
    opts.ScanModulesFromAssembly = typeof(Program).Assembly;  // kendi assembly
});
builder.Services.AddCrudKitModule<ExternalModule>();          // başka assembly
```

#### Kullanıcı Tarafı

```csharp
public class OrderModule : IModule
{
    public string Name => "Orders";

    public void RegisterServices(IServiceCollection services, IConfiguration config)
    {
        services.AddScoped<ICrudHooks<Order>, OrderHooks>();
        services.AddScoped<IEntityMapper<Order, OrderResponse>, OrderMapper>();
    }

    public void MapEndpoints(WebApplication app)
    {
        app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders")
           .RequireAuth();
        app.MapCrudDetailEndpoints<Order, OrderItem, CreateOrderItem>("orders", "items", "OrderId");
    }

    public void RegisterWorkflowActions(ActionRegistry registry)
    {
        registry.Register<OrderActions>();
    }
}
```

#### Testler

```csharp
// ---- ModuleTests.cs (CrudKit.Api.Tests) ----
public class ModuleTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task AutoScan_ShouldRegisterAllModules()
    {
        // ScanModulesFromAssembly ile 3 modül var
        // Her birinin endpoint'leri map edilmeli
    }

    [Fact]
    public async Task ManualRegistration_ShouldWork()
    {
        // AddCrudKitModule<OrderModule>() ile kayıt
        // OrderModule endpoint'leri erişilebilir olmalı
    }

    [Fact]
    public async Task Module_RegisterServices_ShouldBeCalled()
    {
        // IModule.RegisterServices çağrıldı mı?
        // Hook ve mapper'lar DI'da var mı?
    }
}
```

---

### 11.28 DefaultInclude — EF Include + Response Include

`[DefaultInclude]` attribute'u hem EF Core `.Include()` hem JSON response serialization için kullanılır. İki concern tek attribute'ta birleşir çünkü:

- **Mapper kullananlar** zaten bu attribute'u kullanmaz — mapper neyin döneceğini ve neyin yükleneceğini belirler
- **Mapper kullanmayanlar** için ikisini ayırt etmek gereksiz karmaşıklık

#### EfRepo — DefaultInclude ile Otomatik Include

```csharp
public async Task<Paginated<T>> List(ListParams listParams, CancellationToken ct = default)
{
    var query = _db.Set<T>().AsNoTracking();

    // [DefaultInclude] olan navigation property'leri otomatik include et
    foreach (var prop in typeof(T).GetProperties()
        .Where(p => p.GetCustomAttribute<DefaultIncludeAttribute>() != null))
    {
        query = query.Include(prop.Name);
    }

    // Hook'tan scope filtresi
    query = _hooks.ApplyScope(query, _appContext);

    return await _queryBuilder.Apply(query, listParams, ct);
}
```

#### JSON Serialization — DefaultInclude Olmayan Navigation Skip

```csharp
// IgnoreCycles güvenlik ağı olarak aktif
// [DefaultInclude] olmayan IEntity property'leri serialize edilmez
// Bu davranış SkipResponse convention ile sağlanır:
// Navigation property'ler varsayılan [SkipResponse] gibi davranır
// [DefaultInclude] varsa serialize edilir
```

#### Kullanım

```csharp
public class Order : IEntity
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;  // scalar → her zaman

    [DefaultInclude]
    public List<OrderItem> Items { get; set; } = new();     // EF Include + response'a dahil

    public Customer Customer { get; set; } = null!;         // EF Include yok, response'da yok
}
```

#### ApplyIncludes Hook (İleri Seviye)

Daha ince kontrol gerekiyorsa `ICrudHooks<T>.ApplyIncludes` override edilir:

```csharp
public interface ICrudHooks<T> where T : class, IEntity
{
    // ...
    /// <summary>
    /// EF Core Include'larını özelleştirir.
    /// [DefaultInclude] attribute'larından önce uygulanır.
    /// </summary>
    IQueryable<T> ApplyIncludes(IQueryable<T> query) => query;
}

// Kullanıcı tarafı — ThenInclude gibi karmaşık include'lar için
public class OrderHooks : ICrudHooks<Order>
{
    public IQueryable<Order> ApplyIncludes(IQueryable<Order> query)
        => query
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .Include(o => o.Customer);
}
```

#### Testler

```csharp
// ---- DefaultIncludeTests.cs (CrudKit.EntityFrameworkCore.Tests) ----
public class DefaultIncludeTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task List_ShouldInclude_DefaultIncludeProperties()
    {
        // Order.Items [DefaultInclude] var → Items yüklenmeli
        var orders = await _repo.List(new ListParams());
        Assert.All(orders.Data, o => Assert.NotNull(o.Items));
    }

    [Fact]
    public async Task List_ShouldNotInclude_WithoutDefaultInclude()
    {
        // Order.Customer [DefaultInclude] yok → Customer null olmalı
        var orders = await _repo.List(new ListParams());
        Assert.All(orders.Data, o => Assert.Null(o.Customer));
    }

    [Fact]
    public async Task Response_ShouldSerialize_DefaultIncludeNavigation()
    {
        var response = await _client.GetAsync("/api/orders/123");
        var obj = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(obj.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Response_ShouldNotSerialize_NavigationWithoutDefaultInclude()
    {
        var response = await _client.GetAsync("/api/orders/123");
        var obj = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(obj.TryGetProperty("customer", out _));
    }
}
```

---

### 11.29 Health Check — Kaldırıldı

**Karar:** `HealthEndpoint.cs` kaldırıldı. ASP.NET Core built-in health checks yeterlidir.

```csharp
// Kullanıcı tarafı — iki satır
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

app.MapHealthChecks("/health");
```

CrudKit'in özel bir health endpoint sunması gereksiz — ASP.NET Core ekosistemi bu ihtiyacı tam karşılıyor.

---

