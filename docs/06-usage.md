## 6. Kullanıcı Tarafı Kullanım Özeti

### 6.1 Entity Tanımı

```csharp
[CrudEntity(Table = "users", SoftDelete = true, Audit = true)]
public class User : IEntity, ISoftDeletable
{
    public string Id { get; set; } = string.Empty;

    [Required, MaxLength(50), Searchable, Unique]
    public string Username { get; set; } = string.Empty;

    [Required, EmailAddress, Searchable, Unique]
    public string Email { get; set; } = string.Empty;

    [Required, Hashed, SkipResponse]
    public string PasswordHash { get; set; } = string.Empty;

    [UiHint("select")]
    public string Role { get; set; } = "user";

    [Range(0, 150)]
    public int? Age { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

### 6.2 DTO Tanımı

```csharp
public record CreateUser(
    string Username,
    string Email,
    string PasswordHash,
    string? Role = "user",
    int? Age = null,
    bool IsActive = true
);

public record UpdateUser(
    string? Username = null,
    string? Email = null,
    string? Role = null,
    int? Age = null,
    bool? IsActive = null
);
```

### 6.3 Hook'lar

```csharp
public class UserHooks : ICrudHooks<User>
{
    public Task BeforeCreate(User entity, AppContext ctx)
    {
        entity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(entity.PasswordHash);
        entity.Role = "user";
        return Task.CompletedTask;
    }

    // Diğer hook'lar default (boş) — override etmeye gerek yok
}

// DI'a kayıt:
// services.AddScoped<ICrudHooks<User>, UserHooks>();
// Hook kayıtlı değilse CrudKit boş default kullanır.
```

### 6.4 Program.cs

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Kullanıcının kendi DbContext'i
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// 2. Auth — kullanıcı kendi auth'unu kurar
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        options.Audience = builder.Configuration["Auth:Audience"];
    });
builder.Services.AddScoped<ICurrentUser, JwtCurrentUser>();

// 3. CrudKit — kullanıcının DbContext'ini alır, auth'a karışmaz
builder.Services.AddCrudKit<AppDbContext>();

// Hook'lar
builder.Services.AddScoped<ICrudHooks<User>, UserHooks>();
builder.Services.AddScoped<ICrudHooks<Order>, OrderHooks>();

// Workflow (opsiyonel)
builder.Services.AddCrudWorkflow(options =>
{
    options.TimeoutCheckInterval = TimeSpan.FromMinutes(1);
});
builder.Services.AddSingleton<ActionRegistry>(sp =>
{
    var registry = new ActionRegistry();
    registry.Register<PurchaseActions>();
    registry.Register<InvoiceActions>();
    registry.Register<CommonActions>();
    return registry;
});

var app = builder.Build();

app.UseAuthentication();    // kullanıcının kendi auth middleware'i
app.UseAuthorization();
app.UseCrudKit();           // CrudKit middleware (error handling)

// Entity endpoint'leri
app.MapCrudEndpoints<User, CreateUser, UpdateUser>("users");
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products");
app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders");
app.MapCrudEndpoints<Invoice, CreateInvoice, UpdateInvoice>("invoices");
app.MapCrudEndpoints<Category, CreateCategory, UpdateCategory>("categories");

// Detail endpoint'leri
app.MapCrudDetailEndpoints<Order, OrderItem, CreateOrderItem>("orders", "items", "OrderId");
app.MapCrudDetailEndpoints<Invoice, InvoiceLine, CreateInvoiceLine>("invoices", "lines", "InvoiceId");

// Workflow endpoint'leri
app.MapWorkflowEndpoints<PurchaseOrder>("purchase-orders");

app.Run();
```

### 6.5 Üretilen Endpoint'ler

```
── users ──
GET    /api/users                           Listele (filtreleme + sayfalama)
GET    /api/users/{id}                      Tek kayıt
POST   /api/users                           Oluştur
PUT    /api/users/{id}                      Güncelle (partial)
DELETE /api/users/{id}                      Sil (soft delete)
POST   /api/users/{id}/restore              Geri yükle

── orders ──
GET    /api/orders                          Listele
POST   /api/orders                          Oluştur
GET    /api/orders/{id}                     Tek kayıt
PUT    /api/orders/{id}                     Güncelle
DELETE /api/orders/{id}                     Sil

── orders/items (master-detail) ──
GET    /api/orders/{orderId}/items          Siparişin kalemleri
POST   /api/orders/{orderId}/items          Kalem ekle
GET    /api/orders/{orderId}/items/{id}     Tek kalem (sahiplik kontrolü)
PUT    /api/orders/{orderId}/items/batch    Toplu kaydet
DELETE /api/orders/{orderId}/items/{id}     Kalem sil

── purchase-orders (workflow) ──
GET    /api/purchase-orders/{id}/workflow              Workflow durumu
POST   /api/purchase-orders/{id}/workflow/approve/{s}  Onayla
POST   /api/purchase-orders/{id}/workflow/reject/{s}   Reddet
GET    /api/purchase-orders/{id}/workflow/history       Geçmiş
POST   /api/purchase-orders/{id}/workflow/cancel        İptal

```

### 6.6 Filtreleme Kullanımı

```
GET /api/users?username=eq:ali&age=gte:18&is_active=true&sort=-created_at,username&page=1&per_page=25
GET /api/products?price=lte:500&stock=gt:0&name=like:laptop&sort=-price
GET /api/orders?status=neq:cancelled&total=gte:100&created_at=gte:2025-01-01
GET /api/orders?status=in:pending,processing&sort=-created_at
GET /api/users?email=starts:admin&role=in:admin,manager
```

---

