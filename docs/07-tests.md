## 7. Test Dosyaları

### 7.1 CrudKit.Core.Tests

```
CrudKit.Core.Tests/
├── Models/
│   ├── FilterOpTests.cs
│   ├── ListParamsTests.cs
│   ├── ValidationErrorsTests.cs
│   ├── OptionalTests.cs
│   └── PaginatedTests.cs
├── Attributes/
│   └── AttributeMetadataTests.cs
└── CrudKit.Core.Tests.csproj
```

```csharp
// ---- FilterOpTests.cs ----
public class FilterOpTests
{
    [Theory]
    [InlineData("ali", "eq", "ali")]
    [InlineData("eq:ali", "eq", "ali")]
    [InlineData("neq:cancelled", "neq", "cancelled")]
    [InlineData("gt:10", "gt", "10")]
    [InlineData("gte:18", "gte", "18")]
    [InlineData("lt:100", "lt", "100")]
    [InlineData("lte:99.9", "lte", "99.9")]
    [InlineData("like:gmail", "like", "gmail")]
    [InlineData("starts:admin", "starts", "admin")]
    [InlineData("null", "null", "")]
    [InlineData("notnull", "notnull", "")]
    public void Parse_ShouldExtractOperatorAndValue(string raw, string expectedOp, string expectedVal)
    {
        var result = FilterOp.Parse(raw);
        Assert.Equal(expectedOp, result.Operator);
        Assert.Equal(expectedVal, result.Value);
    }

    [Fact]
    public void Parse_InOperator_ShouldSplitValues()
    {
        var result = FilterOp.Parse("in:a,b,c");
        Assert.Equal("in", result.Operator);
        Assert.NotNull(result.Values);
        Assert.Equal(3, result.Values.Count);
        Assert.Equal(new[] { "a", "b", "c" }, result.Values);
    }

    [Fact]
    public void Parse_EmptyString_ShouldReturnEqWithEmpty()
    {
        var result = FilterOp.Parse("");
        Assert.Equal("eq", result.Operator);
        Assert.Equal("", result.Value);
    }
}

// ---- ListParamsTests.cs ----
public class ListParamsTests
{
    [Fact]
    public void FromQuery_ShouldParsePageAndPerPage()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "page", "3" },
            { "per_page", "50" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal(3, result.Page);
        Assert.Equal(50, result.PerPage);
    }

    [Fact]
    public void FromQuery_ShouldClampPerPageToMax100()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "per_page", "500" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal(100, result.PerPage);
    }

    [Fact]
    public void FromQuery_ShouldDefaultToPage1PerPage20()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>());
        var result = ListParams.FromQuery(query);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PerPage);
    }

    [Fact]
    public void FromQuery_ShouldParseSort()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "sort", "-created_at,username" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal("-created_at,username", result.Sort);
    }

    [Fact]
    public void FromQuery_ShouldExtractFilters()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            { "page", "1" },
            { "sort", "-id" },
            { "username", "eq:ali" },
            { "age", "gte:18" }
        });

        var result = ListParams.FromQuery(query);
        Assert.Equal(2, result.Filters.Count);
        Assert.True(result.Filters.ContainsKey("username"));
        Assert.True(result.Filters.ContainsKey("age"));
        Assert.Equal("eq", result.Filters["username"].Operator);
        Assert.Equal("gte", result.Filters["age"].Operator);
    }
}

// ---- ValidationErrorsTests.cs ----
public class ValidationErrorsTests
{
    [Fact]
    public void NewInstance_ShouldBeEmpty()
    {
        var errors = new ValidationErrors();
        Assert.True(errors.IsEmpty);
    }

    [Fact]
    public void Add_ShouldMakeNonEmpty()
    {
        var errors = new ValidationErrors();
        errors.Add("email", "required", "Email zorunludur");
        Assert.False(errors.IsEmpty);
        Assert.Single(errors.Errors);
    }

    [Fact]
    public void ThrowIfInvalid_ShouldNotThrowWhenEmpty()
    {
        var errors = new ValidationErrors();
        errors.ThrowIfInvalid();  // exception fırlatmamalı
    }

    [Fact]
    public void ThrowIfInvalid_ShouldThrowWhenNotEmpty()
    {
        var errors = new ValidationErrors();
        errors.Add("name", "required", "İsim zorunludur");
        Assert.Throws<ValidationException>(() => errors.ThrowIfInvalid());
    }

    [Fact]
    public void MultipleErrors_ShouldAccumulate()
    {
        var errors = new ValidationErrors();
        errors.Add("email", "required", "Email zorunludur");
        errors.Add("email", "email", "Geçerli email giriniz");
        errors.Add("age", "min", "Yaş 0'dan büyük olmalı");
        Assert.Equal(3, errors.Errors.Count);
    }
}

// ---- AttributeMetadataTests.cs ----
public class AttributeMetadataTests
{
    [CrudEntity(Table = "test_users", SoftDelete = true, Audit = true)]
    private class TestUser : IEntity, ISoftDeletable
    {
        public string Id { get; set; } = "";
        [Required, MaxLength(50), Searchable, Unique]
        public string Username { get; set; } = "";
        [SkipResponse, Hashed]
        public string Password { get; set; } = "";
        [Protected]
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    [Fact]
    public void CrudEntityAttribute_ShouldBeReadable()
    {
        var attr = typeof(TestUser).GetCustomAttribute<CrudEntityAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("test_users", attr.Table);
        Assert.True(attr.SoftDelete);
        Assert.True(attr.Audit);
    }

    [Fact]
    public void SearchableAttribute_ShouldBeOnCorrectProperties()
    {
        var searchable = typeof(TestUser).GetProperties()
            .Where(p => p.GetCustomAttribute<SearchableAttribute>() != null)
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("Username", searchable);
        Assert.DoesNotContain("Password", searchable);
    }

    [Fact]
    public void SkipResponseAttribute_ShouldBeOnPassword()
    {
        var prop = typeof(TestUser).GetProperty("Password");
        Assert.NotNull(prop?.GetCustomAttribute<SkipResponseAttribute>());
    }

    [Fact]
    public void ProtectedAttribute_ShouldBeOnStatus()
    {
        var prop = typeof(TestUser).GetProperty("Status");
        Assert.NotNull(prop?.GetCustomAttribute<ProtectedAttribute>());
    }
}
```

### 7.2 CrudKit.EntityFrameworkCore.Tests

```
CrudKit.EntityFrameworkCore.Tests/
├── TestEntities/
│   ├── TestProduct.cs
│   ├── CreateTestProduct.cs
│   ├── UpdateTestProduct.cs
│   └── TestDbContext.cs
├── DbContext/
│   ├── SoftDeleteFilterTests.cs
│   ├── TenantFilterTests.cs
│   ├── TimestampTests.cs
│   ├── AuditLogTests.cs
│   ├── UniqueIndexTests.cs
│   ├── CascadeSoftDeleteTests.cs
│   └── MigrationTests.cs
├── Repository/
│   ├── EfRepoCreateTests.cs
│   ├── EfRepoReadTests.cs
│   ├── EfRepoUpdateTests.cs
│   ├── EfRepoDeleteTests.cs
│   ├── EfRepoSoftDeleteTests.cs
│   └── PartialUpdateTests.cs
├── Query/
│   ├── FilterApplierTests.cs
│   ├── SortApplierTests.cs
│   ├── QueryBuilderTests.cs
│   └── IncludeApplierTests.cs
├── Dialect/
│   ├── DialectDetectorTests.cs
│   └── DialectFilterTests.cs
├── Numbering/
│   └── SequenceGeneratorTests.cs
├── Fixtures/
│   └── DatabaseFixture.cs
└── CrudKit.EntityFrameworkCore.Tests.csproj
```

```csharp
// ---- TestEntities ----
[CrudEntity(Table = "test_products", SoftDelete = true, Audit = true)]
public class TestProduct : IEntity, ISoftDeletable, IAuditable
{
    public string Id { get; set; } = string.Empty;
    [Required, Searchable]
    public string Name { get; set; } = string.Empty;
    [Required, Unique]
    public string Sku { get; set; } = string.Empty;
    [Range(0, double.MaxValue)]
    public decimal Price { get; set; }
    [Range(0, int.MaxValue)]
    public int Stock { get; set; }
    public string? CategoryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public record CreateTestProduct(string Name, string Sku, decimal Price, int Stock = 0, string? CategoryId = null);
public record UpdateTestProduct(string? Name = null, string? Sku = null, decimal? Price = null, int? Stock = null);

public class TestDbContext : CrudKitDbContext
{
    public DbSet<TestProduct> Products => Set<TestProduct>();
    public TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser)
        : base(options, currentUser) { }
}

// ---- DatabaseFixture ----
// SQLite in-memory ile test veritabanı oluşturur.
// FakeCurrentUser ile ICurrentUser sağlar.
public class DatabaseFixture : IAsyncLifetime
{
    public TestDbContext DbContext { get; private set; } = null!;
    public FakeCurrentUser CurrentUser { get; private set; } = null!;
    public EfRepo<TestDbContext, TestProduct> Repo { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        CurrentUser = new FakeCurrentUser
        {
            Id = "test-user-1",
            Username = "testuser",
            TenantId = "tenant-1",
            Roles = new List<string> { "admin" },
        };

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        DbContext = new TestDbContext(options, CurrentUser);
        await DbContext.Database.OpenConnectionAsync();
        await DbContext.Database.EnsureCreatedAsync();

        var dialect = new SqliteDialect();
        var filterApplier = new FilterApplier(dialect);
        var queryBuilder = new QueryBuilder<TestProduct>(filterApplier);
        Repo = new EfRepo<TestDbContext, TestProduct>(DbContext, queryBuilder, dialect);
    }

    public async Task DisposeAsync()
    {
        await DbContext.Database.CloseConnectionAsync();
        await DbContext.DisposeAsync();
    }
}

// ---- TimestampTests.cs ----
public class TimestampTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public TimestampTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task Create_ShouldAutoGenerateId()
    {
        var product = new TestProduct { Name = "AutoId", Sku = "AI-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();
        Assert.False(string.IsNullOrEmpty(product.Id));
    }

    [Fact]
    public async Task Create_ShouldSetTimestampsAsUtc()
    {
        var product = new TestProduct { Name = "UTC", Sku = "UTC-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        Assert.True(product.CreatedAt > DateTime.MinValue);
        Assert.Equal(product.CreatedAt, product.UpdatedAt);
        Assert.Equal(DateTimeKind.Utc, product.CreatedAt.Kind);
    }

    [Fact]
    public async Task Update_ShouldChangeUpdatedAtButNotCreatedAt()
    {
        var product = new TestProduct { Name = "Original", Sku = "UC-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        var originalCreatedAt = product.CreatedAt;
        await Task.Delay(10);

        product.Name = "Modified";
        await _f.DbContext.SaveChangesAsync();

        Assert.Equal(originalCreatedAt, product.CreatedAt);
        Assert.True(product.UpdatedAt > originalCreatedAt);
    }
}

// ---- SoftDeleteFilterTests.cs ----
public class SoftDeleteFilterTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public SoftDeleteFilterTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task Delete_ShouldSetDeletedAtInsteadOfRemoving()
    {
        var product = new TestProduct { Name = "Soft", Sku = "SD-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        _f.DbContext.Products.Remove(product);
        await _f.DbContext.SaveChangesAsync();

        var raw = await _f.DbContext.Products
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == product.Id);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task GlobalFilter_ShouldExcludeDeletedFromQueries()
    {
        var product = new TestProduct { Name = "Hidden", Sku = "GF-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        _f.DbContext.Products.Remove(product);
        await _f.DbContext.SaveChangesAsync();

        var results = await _f.DbContext.Products.ToListAsync();
        Assert.DoesNotContain(results, p => p.Id == product.Id);

        var allResults = await _f.DbContext.Products.IgnoreQueryFilters().ToListAsync();
        Assert.Contains(allResults, p => p.Id == product.Id);
    }
}

// ---- AuditLogTests.cs ----
public class AuditLogTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public AuditLogTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task Create_ShouldWriteAuditLog()
    {
        var product = new TestProduct { Name = "Audited", Sku = "AU-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        var log = await _f.DbContext.AuditLogs
            .FirstOrDefaultAsync(l => l.EntityId == product.Id && l.Action == "Create");
        Assert.NotNull(log);
        Assert.Equal("TestProduct", log.EntityType);
        Assert.Equal(_f.CurrentUser.Id, log.UserId);
    }

    [Fact]
    public async Task Update_ShouldLogChangedFields()
    {
        var product = new TestProduct { Name = "Before", Sku = "AU-002", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        product.Name = "After";
        product.Price = 20;
        await _f.DbContext.SaveChangesAsync();

        var log = await _f.DbContext.AuditLogs
            .FirstOrDefaultAsync(l => l.EntityId == product.Id && l.Action == "Update");
        Assert.NotNull(log);
        Assert.Contains("Name", log.ChangedFields!);
        Assert.Contains("Price", log.ChangedFields!);
    }
}

// ---- UniqueIndexTests.cs ----
public class UniqueIndexTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public UniqueIndexTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task ShouldPreventDuplicateUniqueSku()
    {
        _f.DbContext.Products.Add(new TestProduct { Name = "First", Sku = "UQ-001", Price = 10 });
        await _f.DbContext.SaveChangesAsync();

        _f.DbContext.Products.Add(new TestProduct { Name = "Duplicate", Sku = "UQ-001", Price = 20 });
        await Assert.ThrowsAsync<DbUpdateException>(() => _f.DbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SoftDeletedRecord_ShouldNotBlockNewUniqueSku()
    {
        var product = new TestProduct { Name = "Old", Sku = "REUSE-001", Price = 10 };
        _f.DbContext.Products.Add(product);
        await _f.DbContext.SaveChangesAsync();

        _f.DbContext.Products.Remove(product);  // soft delete
        await _f.DbContext.SaveChangesAsync();

        // Aynı Sku ile yeni kayıt — partial unique index sayesinde hata vermez
        _f.DbContext.Products.Add(new TestProduct { Name = "New", Sku = "REUSE-001", Price = 20 });
        await _f.DbContext.SaveChangesAsync();
    }
}

// ---- EfRepoCreateTests.cs ----
public class EfRepoCreateTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public EfRepoCreateTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task Create_ShouldGenerateIdAndTimestamps()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Laptop", "LAP-001", 999.99m));

        Assert.NotNull(product.Id);
        Assert.NotEqual(string.Empty, product.Id);
        Assert.True(product.CreatedAt > DateTime.MinValue);
        Assert.True(product.UpdatedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task Create_ShouldMapDtoFieldsToEntity()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Mouse", "MOU-001", 29.99m, 100));

        Assert.Equal("Mouse", product.Name);
        Assert.Equal("MOU-001", product.Sku);
        Assert.Equal(29.99m, product.Price);
        Assert.Equal(100, product.Stock);
    }

    [Fact]
    public async Task Create_ShouldBeRetrievableById()
    {
        var created = await _f.Repo.Create(new CreateTestProduct("Keyboard", "KEY-001", 49.99m));
        var found = await _f.Repo.FindById(created.Id);

        Assert.Equal(created.Id, found.Id);
        Assert.Equal("Keyboard", found.Name);
    }
}

// ---- EfRepoReadTests.cs ----
public class EfRepoReadTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public EfRepoReadTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task FindById_ShouldThrowWhenNotFound()
    {
        await Assert.ThrowsAsync<AppError>(() => _f.Repo.FindById("nonexistent"));
    }

    [Fact]
    public async Task FindByIdOrDefault_ShouldReturnNullWhenNotFound()
    {
        var result = await _f.Repo.FindByIdOrDefault("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task List_ShouldReturnPaginatedResult()
    {
        // Seed 25 product
        for (int i = 0; i < 25; i++)
            await _f.Repo.Create(new CreateTestProduct($"Product {i}", $"SKU-{i:D3}", 10m + i));

        var result = await _f.Repo.List(new ListParams { Page = 1, PerPage = 10 });

        Assert.Equal(10, result.Data.Count);
        Assert.Equal(25, result.Total);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task List_ShouldRespectPerPageLimit()
    {
        var result = await _f.Repo.List(new ListParams { Page = 1, PerPage = 5 });
        Assert.True(result.Data.Count <= 5);
    }

    [Fact]
    public async Task Exists_ShouldReturnTrueForExistingEntity()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Exists Test", "EXT-001", 10m));
        Assert.True(await _f.Repo.Exists(product.Id));
    }

    [Fact]
    public async Task Exists_ShouldReturnFalseForNonExistingEntity()
    {
        Assert.False(await _f.Repo.Exists("does-not-exist"));
    }
}

// ---- EfRepoUpdateTests.cs ----
public class EfRepoUpdateTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public EfRepoUpdateTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task Update_ShouldOnlyUpdateProvidedFields()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Original", "UPD-001", 100m, 50));

        var updated = await _f.Repo.Update(product.Id, new UpdateTestProduct(Price: 150m));

        Assert.Equal("Original", updated.Name);    // değişmedi
        Assert.Equal("UPD-001", updated.Sku);       // değişmedi
        Assert.Equal(150m, updated.Price);           // güncellendi
        Assert.Equal(50, updated.Stock);             // değişmedi
    }

    [Fact]
    public async Task Update_ShouldUpdateTimestamp()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Timestamp Test", "TST-001", 10m));
        var originalUpdatedAt = product.UpdatedAt;

        await Task.Delay(10); // küçük gecikme
        var updated = await _f.Repo.Update(product.Id, new UpdateTestProduct(Name: "Updated"));

        Assert.True(updated.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public async Task Update_ShouldThrowWhenNotFound()
    {
        await Assert.ThrowsAsync<AppError>(() =>
            _f.Repo.Update("nonexistent", new UpdateTestProduct(Name: "test")));
    }
}

// ---- EfRepoDeleteTests.cs ----
public class EfRepoDeleteTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public EfRepoDeleteTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task Delete_ShouldRemoveEntity()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Delete Me", "DEL-001", 10m));
        await _f.Repo.Delete(product.Id);

        // Soft delete olduğu için FindById bulamaz ama DB'de var
        var found = await _f.Repo.FindByIdOrDefault(product.Id);
        Assert.Null(found);
    }

    [Fact]
    public async Task Delete_ShouldThrowWhenNotFound()
    {
        await Assert.ThrowsAsync<AppError>(() => _f.Repo.Delete("nonexistent"));
    }
}

// ---- EfRepoSoftDeleteTests.cs ----
public class EfRepoSoftDeleteTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public EfRepoSoftDeleteTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task SoftDelete_ShouldSetDeletedAt()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Soft Del", "SFT-001", 10m));
        await _f.Repo.Delete(product.Id);

        // Doğrudan DB'den sorgula (soft delete filtresi olmadan)
        var raw = await _f.DbContext.Products.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == product.Id);

        Assert.NotNull(raw);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task SoftDelete_ShouldExcludeFromList()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Listed", "LST-001", 10m));
        await _f.Repo.Delete(product.Id);

        var list = await _f.Repo.List(new ListParams());
        Assert.DoesNotContain(list.Data, p => p.Id == product.Id);
    }

    [Fact]
    public async Task Restore_ShouldClearDeletedAt()
    {
        var product = await _f.Repo.Create(new CreateTestProduct("Restore Me", "RST-001", 10m));
        await _f.Repo.Delete(product.Id);
        await _f.Repo.Restore(product.Id);

        var found = await _f.Repo.FindByIdOrDefault(product.Id);
        Assert.NotNull(found);
        Assert.Null(found.DeletedAt);
    }
}

// ---- FilterApplierTests.cs ----
public class FilterApplierTests
{
    // In-memory DbContext veya List<T>.AsQueryable() kullanarak test

    [Fact]
    public void Eq_ShouldFilterExactMatch()
    {
        var data = TestData().AsQueryable();
        var filters = new Dictionary<string, FilterOp>
        {
            { "Name", FilterOp.Parse("eq:Laptop") }
        };

        var result = FilterApplier.Apply(data, filters).ToList();
        Assert.All(result, p => Assert.Equal("Laptop", p.Name));
    }

    [Fact]
    public void Gte_ShouldFilterGreaterOrEqual()
    {
        var data = TestData().AsQueryable();
        var filters = new Dictionary<string, FilterOp>
        {
            { "Price", FilterOp.Parse("gte:100") }
        };

        var result = FilterApplier.Apply(data, filters).ToList();
        Assert.All(result, p => Assert.True(p.Price >= 100));
    }

    [Fact]
    public void Like_ShouldFilterContains()
    {
        var data = TestData().AsQueryable();
        var filters = new Dictionary<string, FilterOp>
        {
            { "Name", FilterOp.Parse("like:top") }
        };

        var result = FilterApplier.Apply(data, filters).ToList();
        Assert.All(result, p => Assert.Contains("top", p.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void In_ShouldFilterMultipleValues()
    {
        var data = TestData().AsQueryable();
        var filters = new Dictionary<string, FilterOp>
        {
            { "Sku", FilterOp.Parse("in:SKU-001,SKU-003") }
        };

        var result = FilterApplier.Apply(data, filters).ToList();
        Assert.All(result, p => Assert.True(p.Sku == "SKU-001" || p.Sku == "SKU-003"));
    }

    [Fact]
    public void MultipleFilters_ShouldApplyAll()
    {
        var data = TestData().AsQueryable();
        var filters = new Dictionary<string, FilterOp>
        {
            { "Price", FilterOp.Parse("gte:50") },
            { "Stock", FilterOp.Parse("gt:0") }
        };

        var result = FilterApplier.Apply(data, filters).ToList();
        Assert.All(result, p =>
        {
            Assert.True(p.Price >= 50);
            Assert.True(p.Stock > 0);
        });
    }

    [Fact]
    public void UnknownField_ShouldBeIgnored()
    {
        var data = TestData().AsQueryable();
        var filters = new Dictionary<string, FilterOp>
        {
            { "NonExistentField", FilterOp.Parse("eq:something") }
        };

        var result = FilterApplier.Apply(data, filters).ToList();
        Assert.Equal(data.Count(), result.Count);  // filtre uygulanmadı
    }

    private static List<TestProduct> TestData() => new()
    {
        new() { Id = "1", Name = "Laptop", Sku = "SKU-001", Price = 999, Stock = 10 },
        new() { Id = "2", Name = "Desktop", Sku = "SKU-002", Price = 1299, Stock = 5 },
        new() { Id = "3", Name = "Mouse", Sku = "SKU-003", Price = 29, Stock = 100 },
        new() { Id = "4", Name = "Keyboard", Sku = "SKU-004", Price = 59, Stock = 0 },
    };
}

// ---- SortApplierTests.cs ----
public class SortApplierTests
{
    [Fact]
    public void ShouldSortAscByDefault()
    {
        var data = TestData().AsQueryable();
        var result = SortApplier.Apply(data, "price").ToList();
        Assert.True(result[0].Price <= result[1].Price);
    }

    [Fact]
    public void ShouldSortDescWithMinusPrefix()
    {
        var data = TestData().AsQueryable();
        var result = SortApplier.Apply(data, "-price").ToList();
        Assert.True(result[0].Price >= result[1].Price);
    }

    [Fact]
    public void ShouldSupportMultipleFields()
    {
        var data = TestData().AsQueryable();
        var result = SortApplier.Apply(data, "stock,-price").ToList();
        // stock ASC, stock eşitse price DESC
        Assert.NotEmpty(result);
    }

    [Fact]
    public void InvalidField_ShouldBeIgnored()
    {
        var data = TestData().AsQueryable();
        var result = SortApplier.Apply(data, "nonexistent").ToList();
        Assert.Equal(data.Count(), result.Count);
    }

    [Fact]
    public void NullSort_ShouldDefaultToCreatedAtDesc()
    {
        var data = TestData().AsQueryable();
        var result = SortApplier.Apply(data, null).ToList();
        Assert.NotEmpty(result);
    }
}

// ---- SequenceGeneratorTests.cs ----
public class SequenceGeneratorTests : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task ShouldGenerateSequentialNumbers()
    {
        var gen = new SequenceGenerator(/* db */);
        var num1 = await gen.Next<TestInvoice>("tenant1");
        var num2 = await gen.Next<TestInvoice>("tenant1");

        // FTR-2026-00001, FTR-2026-00002
        Assert.NotEqual(num1, num2);
        Assert.StartsWith("FTR-", num1);
        Assert.StartsWith("FTR-", num2);
    }

    [Fact]
    public async Task ShouldIsolateByTenant()
    {
        var gen = new SequenceGenerator(/* db */);
        var num1 = await gen.Next<TestInvoice>("tenant1");
        var num2 = await gen.Next<TestInvoice>("tenant2");

        // İkisi de 00001 olmalı çünkü farklı tenant
        Assert.Contains("00001", num1);
        Assert.Contains("00001", num2);
    }
}
```

```csharp
// ---- DialectDetectorTests.cs ----
public class DialectDetectorTests
{
    [Fact]
    public void ShouldDetectPostgres()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;
        using var db = new TestDbContext(options);

        var dialect = DialectDetector.Detect(db);
        Assert.IsType<PostgresDialect>(dialect);
    }

    [Fact]
    public void ShouldDetectSqlServer()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer("Server=localhost")
            .Options;
        using var db = new TestDbContext(options);

        var dialect = DialectDetector.Detect(db);
        Assert.IsType<SqlServerDialect>(dialect);
    }

    [Fact]
    public void ShouldDetectSqlite()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        using var db = new TestDbContext(options);

        var dialect = DialectDetector.Detect(db);
        Assert.IsType<SqliteDialect>(dialect);
    }

    [Fact]
    public void ShouldFallbackToGenericForUnknownProvider()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase("test")
            .Options;
        using var db = new TestDbContext(options);

        var dialect = DialectDetector.Detect(db);
        Assert.IsType<GenericDialect>(dialect);
    }
}

// ---- DialectFilterTests.cs ----
// Her dialect'in like/startsWith davranışını gerçek DB ile test eder.
// SQLite in-memory ile çalışır, diğer provider'lar CI'da test edilir.
public class DialectFilterTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _f;
    public DialectFilterTests(DatabaseFixture f) => _f = f;

    [Fact]
    public async Task SqliteDialect_Like_ShouldBeCaseInsensitive()
    {
        // Seed
        await _f.Repo.Create(new CreateTestProduct("Laptop Pro", "LP-001", 999));
        await _f.Repo.Create(new CreateTestProduct("laptop basic", "LB-001", 499));
        await _f.Repo.Create(new CreateTestProduct("Desktop", "DT-001", 799));

        var dialect = new SqliteDialect();
        var query = _f.DbContext.Products.AsQueryable();
        var result = dialect.ApplyLike(query, p => p.Name, "laptop").ToList();

        Assert.Equal(2, result.Count);  // "Laptop Pro" ve "laptop basic"
    }

    [Fact]
    public async Task SqliteDialect_StartsWith_ShouldBeCaseInsensitive()
    {
        var dialect = new SqliteDialect();
        var query = _f.DbContext.Products.AsQueryable();
        var result = dialect.ApplyStartsWith(query, p => p.Name, "lap").ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GenericDialect_Like_ShouldFallbackToContains()
    {
        var dialect = new GenericDialect();
        var data = new List<TestProduct>
        {
            new() { Id = "1", Name = "Hello World" },
            new() { Id = "2", Name = "hello there" },
            new() { Id = "3", Name = "Goodbye" },
        }.AsQueryable();

        var result = dialect.ApplyLike(data, p => p.Name, "hello").ToList();
        Assert.Equal(2, result.Count);
    }
}
```

### 7.4 CrudKit.Api.Tests

```
CrudKit.Api.Tests/
├── Endpoints/
│   ├── CrudEndpointTests.cs
│   ├── DetailEndpointTests.cs
│   ├── SchemaEndpointTests.cs
│   ├── TransactionTests.cs
│   ├── BulkOperationTests.cs
│   ├── OperationControlTests.cs
│   ├── ConcurrencyTests.cs
│   └── ApiVersioningTests.cs
├── Filters/
│   ├── ValidationFilterTests.cs
│   ├── WorkflowProtectionFilterTests.cs
│   ├── IdempotencyTests.cs
│   └── ErrorHandlingTests.cs
├── Middleware/
│   ├── LoggingTests.cs
│   └── HealthCheckTests.cs
├── Metadata/
│   └── SchemaGeneratorTests.cs
├── Fixtures/
│   └── ApiFixture.cs
└── CrudKit.Api.Tests.csproj
```

```csharp
// ---- ApiFixture ----
// WebApplicationFactory kullanarak in-memory test sunucusu oluşturur.
// SQLite in-memory veritabanı ile entegrasyon testi yapılır.
public class ApiFixture : IAsyncLifetime
{
    public HttpClient Client { get; private set; } = null!;
    private WebApplication _app = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<TestDbContext>(opts =>
            opts.UseSqlite("DataSource=:memory:"));
        builder.Services.AddCrudKit<TestDbContext>();
        builder.Services.AddScoped<ICurrentUser>(_ => new FakeCurrentUser
        {
            Id = "test-user-1",
            TenantId = "test-tenant",
            Roles = new List<string> { "admin" },
        });

        _app = builder.Build();
        _app.UseCrudKit();
        _app.MapCrudEndpoints<TestProduct, CreateTestProduct, UpdateTestProduct>("products");
        _app.MapSchemaEndpoint();

        await _app.StartAsync();
        Client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    }

    public async Task DisposeAsync() => await _app.StopAsync();
}

// ---- CrudEndpointTests.cs ----
public class CrudEndpointTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public CrudEndpointTests(ApiFixture f) => _client = f.Client;

    [Fact]
    public async Task POST_ShouldCreateAndReturn201()
    {
        var body = new { Name = "Test", Sku = "TST-001", Price = 10.0, Stock = 5 };
        var response = await _client.PostAsJsonAsync("/api/products", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var product = await response.Content.ReadFromJsonAsync<TestProduct>();
        Assert.NotNull(product);
        Assert.Equal("Test", product.Name);
        Assert.NotEmpty(product.Id);
    }

    [Fact]
    public async Task GET_List_ShouldReturnPaginated()
    {
        var response = await _client.GetAsync("/api/products?page=1&per_page=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<Paginated<TestProduct>>();
        Assert.NotNull(result);
        Assert.True(result.Total >= 0);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task GET_ById_ShouldReturn404WhenNotFound()
    {
        var response = await _client.GetAsync("/api/products/nonexistent");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PUT_ShouldPartialUpdate()
    {
        // Önce oluştur
        var createResponse = await _client.PostAsJsonAsync("/api/products",
            new { Name = "Original", Sku = "PU-001", Price = 100.0 });
        var created = await createResponse.Content.ReadFromJsonAsync<TestProduct>();

        // Sadece price güncelle
        var updateResponse = await _client.PutAsJsonAsync($"/api/products/{created!.Id}",
            new { Price = 200.0 });
        var updated = await updateResponse.Content.ReadFromJsonAsync<TestProduct>();

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal("Original", updated!.Name);  // değişmedi
        Assert.Equal(200.0m, updated.Price);        // güncellendi
    }

    [Fact]
    public async Task DELETE_ShouldReturn200()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/products",
            new { Name = "Delete Me", Sku = "DL-001", Price = 10.0 });
        var created = await createResponse.Content.ReadFromJsonAsync<TestProduct>();

        var deleteResponse = await _client.DeleteAsync($"/api/products/{created!.Id}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var getResponse = await _client.GetAsync($"/api/products/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task POST_ShouldReturn400WhenValidationFails()
    {
        var body = new { Name = "", Sku = "", Price = -10.0 };  // hepsi geçersiz
        var response = await _client.PostAsJsonAsync("/api/products", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GET_List_ShouldFilter()
    {
        // Seed
        await _client.PostAsJsonAsync("/api/products", new { Name = "Cheap", Sku = "F-001", Price = 10.0 });
        await _client.PostAsJsonAsync("/api/products", new { Name = "Expensive", Sku = "F-002", Price = 1000.0 });

        var response = await _client.GetAsync("/api/products?price=gte:500");
        var result = await response.Content.ReadFromJsonAsync<Paginated<TestProduct>>();

        Assert.All(result!.Data, p => Assert.True(p.Price >= 500));
    }

    [Fact]
    public async Task GET_List_ShouldSort()
    {
        var response = await _client.GetAsync("/api/products?sort=-price");
        var result = await response.Content.ReadFromJsonAsync<Paginated<TestProduct>>();

        for (int i = 1; i < result!.Data.Count; i++)
            Assert.True(result.Data[i - 1].Price >= result.Data[i].Price);
    }
}

// ---- SchemaEndpointTests.cs ----
public class SchemaEndpointTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public SchemaEndpointTests(ApiFixture f) => _client = f.Client;

    [Fact]
    public async Task Schema_ShouldReturnRegisteredEntities()
    {
        var response = await _client.GetAsync("/api/_schema");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var schema = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entities = schema.GetProperty("entities");
        Assert.True(entities.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Schema_ShouldContainFieldMetadata()
    {
        var response = await _client.GetAsync("/api/_schema");
        var schema = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entity = schema.GetProperty("entities").EnumerateArray().First();
        var fields = entity.GetProperty("fields");
        Assert.True(fields.GetArrayLength() > 0);

        var field = fields.EnumerateArray().First();
        Assert.True(field.TryGetProperty("name", out _));
        Assert.True(field.TryGetProperty("type", out _));
        Assert.True(field.TryGetProperty("required", out _));
    }
}

// ---- SchemaGeneratorTests.cs ----
public class SchemaGeneratorTests
{
    [Fact]
    public void ShouldExtractSearchableFields()
    {
        var meta = SchemaGenerator.Generate(typeof(TestProduct));
        var searchable = meta.Fields.Where(f => f.Searchable).Select(f => f.Name).ToList();
        Assert.Contains("Name", searchable);
    }

    [Fact]
    public void ShouldExtractUniqueFields()
    {
        var meta = SchemaGenerator.Generate(typeof(TestProduct));
        var unique = meta.Fields.Where(f => f.Unique).Select(f => f.Name).ToList();
        Assert.Contains("Sku", unique);
    }

    [Fact]
    public void ShouldExtractRangeConstraints()
    {
        var meta = SchemaGenerator.Generate(typeof(TestProduct));
        var price = meta.Fields.First(f => f.Name == "Price");
        Assert.Equal(0, price.Min);
    }

    [Fact]
    public void ShouldDetectSoftDeleteFeature()
    {
        var meta = SchemaGenerator.Generate(typeof(TestProduct));
        Assert.True(meta.Features.SoftDelete);
    }
}
```

### 7.5 CrudKit.Workflow.Tests

```
CrudKit.Workflow.Tests/
├── Engine/
│   ├── ActionRegistryTests.cs
│   └── WorkflowEngineTests.cs
├── Models/
│   └── StepDefinitionTests.cs
├── Fixtures/
│   └── WorkflowFixture.cs
└── CrudKit.Workflow.Tests.csproj
```

```csharp
// ---- ActionRegistryTests.cs ----
public class ActionRegistryTests
{
    [Fact]
    public void RegisterAction_ShouldBeRetrievable()
    {
        var registry = new ActionRegistry();
        registry.RegisterAction("test.action", async ctx => "done");

        var action = registry.GetAction("test.action");
        Assert.NotNull(action);
    }

    [Fact]
    public void GetAction_ShouldThrowWhenNotFound()
    {
        var registry = new ActionRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetAction("nonexistent"));
    }

    [Fact]
    public void RegisterCondition_ShouldBeRetrievable()
    {
        var registry = new ActionRegistry();
        registry.RegisterCondition("test.cond", async ctx => "next_step");

        var condition = registry.GetCondition("test.cond");
        Assert.NotNull(condition);
    }

    [Fact]
    public void ListActions_ShouldReturnAllKeys()
    {
        var registry = new ActionRegistry();
        registry.RegisterAction("a.one", async ctx => null);
        registry.RegisterAction("a.two", async ctx => null);
        registry.RegisterAction("b.three", async ctx => null);

        var keys = registry.ListActions();
        Assert.Equal(3, keys.Count);
        Assert.Contains("a.one", keys);
        Assert.Contains("a.two", keys);
        Assert.Contains("b.three", keys);
    }

    [Fact]
    public async Task RegisteredAction_ShouldExecute()
    {
        var registry = new ActionRegistry();
        var executed = false;

        registry.RegisterAction("test.exec", async ctx =>
        {
            executed = true;
            return "result";
        });

        var action = registry.GetAction("test.exec");
        var result = await action(new ActionContext { /* mock */ });

        Assert.True(executed);
        Assert.Equal("result", result);
    }
}

// ---- WorkflowEngineTests.cs ----
public class WorkflowEngineTests : IClassFixture<WorkflowFixture>
{
    private readonly WorkflowFixture _f;
    public WorkflowEngineTests(WorkflowFixture f) => _f = f;

    [Fact]
    public async Task Start_ShouldCreateInstance()
    {
        var instance = await _f.Engine.Start(
            "test_workflow", "test_entity", "entity-1", "tenant-1", "user-1");

        Assert.NotNull(instance);
        Assert.Equal("test_workflow", instance.WorkflowName);
        Assert.Equal("entity-1", instance.EntityId);
        Assert.Equal(WorkflowStatus.Running, instance.Status);
    }

    [Fact]
    public async Task Start_ShouldExecuteFirstStep()
    {
        var instance = await _f.Engine.Start(
            "test_workflow", "test_entity", "entity-1", "tenant-1", "user-1");

        var history = await _f.Engine.GetHistory(instance.Id);
        Assert.NotEmpty(history);
        Assert.Equal("validate", history.First().StepId);
    }

    [Fact]
    public async Task Approve_ShouldAdvanceWorkflow()
    {
        // Setup: workflow approval step'inde bekliyor
        var instance = await _f.Engine.Start(
            "approval_workflow", "test_entity", "entity-2", "tenant-1", "user-1");

        var approver = new FakeCurrentUser { Id = "manager-1", Roles = new List<string> { "manager" } };
        await _f.Engine.Approve(instance.Id, "manager_approval", approver, "Onaylıyorum");

        var updated = await _f.Engine.GetInstance(instance.Id);
        Assert.NotEqual("manager_approval", updated.CurrentStep);  // ilerlemiş olmalı
    }

    [Fact]
    public async Task Reject_ShouldFailWorkflow()
    {
        var instance = await _f.Engine.Start(
            "approval_workflow", "test_entity", "entity-3", "tenant-1", "user-1");

        var approver = new FakeCurrentUser { Id = "manager-1", Roles = new List<string> { "manager" } };
        await _f.Engine.Reject(instance.Id, "manager_approval", approver, "Uygun değil");

        var updated = await _f.Engine.GetInstance(instance.Id);
        Assert.Equal(WorkflowStatus.Failed, updated.Status);
    }

    [Fact]
    public async Task HasActiveInstance_ShouldReturnCorrectly()
    {
        await _f.Engine.Start(
            "test_workflow", "test_entity", "entity-4", "tenant-1", "user-1");

        Assert.True(await _f.Engine.HasActiveInstance("test_entity", "entity-4"));
        Assert.False(await _f.Engine.HasActiveInstance("test_entity", "entity-999"));
    }

    [Fact]
    public async Task Cancel_ShouldSetStatusToCancelled()
    {
        var instance = await _f.Engine.Start(
            "test_workflow", "test_entity", "entity-5", "tenant-1", "user-1");

        await _f.Engine.Cancel(instance.Id, "user-1", "Vazgeçtim");

        var updated = await _f.Engine.GetInstance(instance.Id);
        Assert.Equal(WorkflowStatus.Cancelled, updated.Status);
    }
}
```

---

