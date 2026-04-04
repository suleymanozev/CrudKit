## 12. CrudKit.SourceGen — Compile-Time Kod Üretimi

85 entity için elle `CreateUser`, `UpdateUser`, endpoint mapping yazmak tekrarlayan iş. Source Generator entity sınıfından derleme sırasında otomatik üretir: DTO'lar, endpoint mapping extension, hook stub'ları.

### 12.1 Dosya Yapısı

```
CrudKit.SourceGen/
├── CrudKitSourceGenerator.cs           # Ana generator — IIncrementalGenerator
├── Parsers/
│   ├── EntityParser.cs                 # Entity sınıfından metadata çıkarır
│   ├── AttributeParser.cs             # [CrudEntity], [field] attribute parse
│   └── PropertyAnalyzer.cs            # Property tipi, nullable, attribute analizi
├── Generators/
│   ├── CreateDtoGenerator.cs          # CreateX record üretimi
│   ├── UpdateDtoGenerator.cs          # UpdateX record üretimi (Optional<T>)
│   ├── ResponseDtoGenerator.cs        # XResponse record üretimi ([SkipResponse] hariç)
│   ├── MapperGenerator.cs             # IEntityMapper<T> implementasyonu üretimi
│   ├── EndpointMappingGenerator.cs    # MapAllCrudEndpoints extension üretimi
│   └── HookStubGenerator.cs          # ICrudHooks<T> boş implementasyon üretimi
├── Templates/
│   └── SourceTemplates.cs            # String template'ler
└── CrudKit.SourceGen.csproj
```

### 12.2 Proje Konfigürasyonu

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.*" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" PrivateAssets="all" />
  </ItemGroup>

  <!-- CrudKit.Core'u analyzer olarak referans — runtime'da bağımlılık yok -->
  <ItemGroup>
    <ProjectReference Include="..\CrudKit.Core\CrudKit.Core.csproj"
                      PrivateAssets="all"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

```xml
<!-- Kullanıcı projesinde referans -->
<ItemGroup>
    <PackageReference Include="CrudKit.SourceGen"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
</ItemGroup>
```

### 12.3 Ana Generator

```csharp
[Generator]
public class CrudKitSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // [CrudEntity] attribute'u olan sınıfları bul
        var entityDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "CrudKit.Core.Attributes.CrudEntityAttribute",
                predicate: (node, _) => node is ClassDeclarationSyntax,
                transform: (ctx, _) => ParseEntity(ctx))
            .Where(e => e is not null)
            .Select((e, _) => e!);

        // Her entity için DTO + mapper + endpoint + hook üret
        context.RegisterSourceOutput(entityDeclarations, (spc, entity) =>
        {
            // CreateDto
            var createDto = CreateDtoGenerator.Generate(entity);
            if (createDto != null)
                spc.AddSource($"{entity.Name}.CreateDto.g.cs", createDto);

            // UpdateDto (Optional<T> ile)
            var updateDto = UpdateDtoGenerator.Generate(entity);
            if (updateDto != null)
                spc.AddSource($"{entity.Name}.UpdateDto.g.cs", updateDto);

            // ResponseDto ([SkipResponse] alanlar hariç)
            var responseDto = ResponseDtoGenerator.Generate(entity);
            if (responseDto != null)
                spc.AddSource($"{entity.Name}.ResponseDto.g.cs", responseDto);

            // Mapper (sıfır reflection)
            var mapper = MapperGenerator.Generate(entity);
            if (mapper != null)
                spc.AddSource($"{entity.Name}.Mapper.g.cs", mapper);

            // Hook stub
            var hookStub = HookStubGenerator.Generate(entity);
            spc.AddSource($"{entity.Name}.Hooks.g.cs", hookStub);
        });

        // Tüm entity'leri topluca endpoint mapping + mapper DI
        var allEntities = entityDeclarations.Collect();
        context.RegisterSourceOutput(allEntities, (spc, entities) =>
        {
            var endpointMapping = EndpointMappingGenerator.Generate(entities);
            spc.AddSource("CrudKitEndpoints.g.cs", endpointMapping);

            var mapperRegistration = MapperGenerator.GenerateDiRegistration(entities);
            spc.AddSource("CrudKitMappers.g.cs", mapperRegistration);
        });
    }

    private static EntityMetadata? ParseEntity(GeneratorAttributeSyntaxContext ctx)
    {
        var classSymbol = ctx.TargetSymbol as INamedTypeSymbol;
        if (classSymbol == null) return null;

        return EntityParser.Parse(classSymbol);
    }
}
```

### 12.4 EntityMetadata — Parse edilen bilgi

```csharp
public record EntityMetadata
{
    public string Name { get; init; } = "";                    // "User"
    public string Namespace { get; init; } = "";               // "MyApp.Entities"
    public string FullName { get; init; } = "";                // "MyApp.Entities.User"
    public string Table { get; init; } = "";                   // "users"
    public bool SoftDelete { get; init; }
    public bool Audit { get; init; }
    public bool MultiTenant { get; init; }
    public bool EnableBulkUpdate { get; init; }
    public bool EnableBulkDelete { get; init; }
    public string? Workflow { get; init; }
    public string[]? WorkflowProtected { get; init; }
    public string? NumberingPrefix { get; init; }
    public List<PropertyMetadata> Properties { get; init; } = new();
}

public record PropertyMetadata
{
    public string Name { get; init; } = "";                    // "Username"
    public string Type { get; init; } = "";                    // "string"
    public string FullType { get; init; } = "";                // "System.String"
    public bool IsNullable { get; init; }
    public bool IsRequired { get; init; }                      // [Required] var mı
    public bool IsUnique { get; init; }                        // [Unique] var mı
    public bool IsSearchable { get; init; }                    // [Searchable] var mı
    public bool IsProtected { get; init; }                     // [Protected] var mı
    public bool SkipResponse { get; init; }                    // [SkipResponse] var mı
    public bool SkipUpdate { get; init; }                      // [SkipUpdate] var mı
    public bool IsHashed { get; init; }                        // [Hashed] var mı
    public bool IsSystemField { get; init; }                   // Id, CreatedAt, UpdatedAt, DeletedAt
    public int? MaxLength { get; init; }                       // [MaxLength(50)]
    public double? RangeMin { get; init; }                     // [Range(0, 150)]
    public double? RangeMax { get; init; }
}
```

### 12.5 CreateDto Generator

```csharp
public static class CreateDtoGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        // System alanları hariç (Id, CreatedAt, UpdatedAt, DeletedAt)
        // [SkipUpdate] alanlar dahil (create'de lazım olabilir)
        var props = entity.Properties
            .Where(p => !p.IsSystemField)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine($"namespace {entity.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public record Create{entity.Name}(");

        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            var comma = i < props.Count - 1 ? "," : "";
            var defaultValue = "";

            // Nullable veya required değilse default değer ver
            if (p.IsNullable || !p.IsRequired)
                defaultValue = $" = {GetDefaultValue(p)}";

            // Validation attribute'ları koru
            var attributes = BuildValidationAttributes(p);
            if (!string.IsNullOrEmpty(attributes))
                sb.AppendLine($"    {attributes}");

            sb.AppendLine($"    {p.Type} {p.Name}{defaultValue}{comma}");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }

    private static string GetDefaultValue(PropertyMetadata p)
    {
        if (p.IsNullable) return "null";
        return p.Type switch
        {
            "bool" => "false",
            "int" or "long" or "decimal" or "double" or "float" => "0",
            _ => "null"
        };
    }

    private static string BuildValidationAttributes(PropertyMetadata p)
    {
        var attrs = new List<string>();
        if (p.IsRequired) attrs.Add("[Required]");
        if (p.MaxLength.HasValue) attrs.Add($"[MaxLength({p.MaxLength})]");
        if (p.RangeMin.HasValue || p.RangeMax.HasValue)
            attrs.Add($"[Range({p.RangeMin ?? double.MinValue}, {p.RangeMax ?? double.MaxValue})]");
        return string.Join(" ", attrs);
    }
}
```

```csharp
// ---- Üretilen kod örneği ----

// Entity:
[CrudEntity(Table = "users", SoftDelete = true)]
public class User : IEntity, ISoftDeletable
{
    public string Id { get; set; } = "";
    [Required, MaxLength(50), Searchable, Unique]
    public string Username { get; set; } = "";
    [Required, EmailAddress, Unique]
    public string Email { get; set; } = "";
    [Required, Hashed, SkipResponse]
    public string PasswordHash { get; set; } = "";
    [UiHint("select")]
    public string Role { get; set; } = "user";
    [Range(0, 150)]
    public int? Age { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

// Üretilen CreateUser:
// <auto-generated />
namespace MyApp.Entities;

public record CreateUser(
    [Required] [MaxLength(50)]
    string Username,
    [Required]
    string Email,
    [Required]
    string PasswordHash,
    string Role = "user",
    [Range(0, 150)]
    int? Age = null,
    bool IsActive = true
);
```

### 12.6 UpdateDto Generator

```csharp
public static class UpdateDtoGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        // System alanları hariç
        // [SkipUpdate] alanlar hariç
        // [Protected] alanlar hariç
        // Her alan Optional<T> ile sarılır
        var props = entity.Properties
            .Where(p => !p.IsSystemField && !p.SkipUpdate && !p.IsProtected)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine($"using CrudKit.Core.Models;");
        sb.AppendLine($"namespace {entity.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public record Update{entity.Name}");
        sb.AppendLine("{");

        foreach (var p in props)
        {
            var innerType = p.IsNullable ? p.Type : $"{p.Type}?";
            sb.AppendLine($"    public Optional<{innerType}> {p.Name} {{ get; init; }}");
        }

        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

```csharp
// ---- Üretilen UpdateUser ----
// <auto-generated />
using CrudKit.Core.Models;
namespace MyApp.Entities;

public record UpdateUser
{
    public Optional<string?> Username { get; init; }
    public Optional<string?> Email { get; init; }
    // PasswordHash → [SkipUpdate] → burada yok
    public Optional<string?> Role { get; init; }
    public Optional<int?> Age { get; init; }
    public Optional<bool?> IsActive { get; init; }
    // Status → [Protected] → burada yok
}
```

### 12.7 Endpoint Mapping Generator

```csharp
public static class EndpointMappingGenerator
{
    public static string Generate(ImmutableArray<EntityMetadata> entities)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("using CrudKit.Api.Endpoints;");
        sb.AppendLine();
        sb.AppendLine("namespace CrudKit.Generated;");
        sb.AppendLine();
        sb.AppendLine("public static class CrudKitEndpointExtensions");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// Tüm CrudKit entity'leri için CRUD endpoint'lerini register eder.");
        sb.AppendLine("    /// Source Generator tarafından otomatik üretilmiştir.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    public static WebApplication MapAllCrudEndpoints(this WebApplication app)");
        sb.AppendLine("    {");

        foreach (var entity in entities)
        {
            var route = ToKebabCase(entity.Table);
            sb.AppendLine($"        app.MapCrudEndpoints<{entity.FullName}, Create{entity.Name}, Update{entity.Name}>(\"{route}\");");
        }

        sb.AppendLine("        return app;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string ToKebabCase(string input)
        => Regex.Replace(input, "_", "-");
}
```

```csharp
// ---- Üretilen CrudKitEndpoints.g.cs ----
// <auto-generated />
using CrudKit.Api.Endpoints;

namespace CrudKit.Generated;

public static class CrudKitEndpointExtensions
{
    public static WebApplication MapAllCrudEndpoints(this WebApplication app)
    {
        app.MapCrudEndpoints<MyApp.Entities.User, CreateUser, UpdateUser>("users");
        app.MapCrudEndpoints<MyApp.Entities.Product, CreateProduct, UpdateProduct>("products");
        app.MapCrudEndpoints<MyApp.Entities.Order, CreateOrder, UpdateOrder>("orders");
        app.MapCrudEndpoints<MyApp.Entities.Invoice, CreateInvoice, UpdateInvoice>("invoices");
        app.MapCrudEndpoints<MyApp.Entities.Category, CreateCategory, UpdateCategory>("categories");
        // ... 80 entity daha — otomatik
        return app;
    }
}
```

```csharp
// ---- Program.cs — tek satırda tüm endpoint'ler ----

var app = builder.Build();
app.UseCrudKit();

// Elle yazmak yerine:
// app.MapCrudEndpoints<User, CreateUser, UpdateUser>("users");
// app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products");
// ... 85 satır

// Tek satır:
app.MapAllCrudEndpoints();

app.Run();
```

### 12.8 Hook Stub Generator

```csharp
public static class HookStubGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        // Boş hook implementasyonu üretir.
        // Kullanıcı ihtiyacı olan hook'u override eder.
        // partial class olarak üretilir — kullanıcı ayrı dosyada genişletebilir.

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine($"using CrudKit.Core.Interfaces;");
        sb.AppendLine($"using CrudKit.Core.Context;");
        sb.AppendLine($"namespace {entity.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public partial class {entity.Name}Hooks : ICrudHooks<{entity.Name}>");
        sb.AppendLine("{");
        sb.AppendLine($"    // Bu dosya otomatik üretilmiştir.");
        sb.AppendLine($"    // Hook'ları override etmek için partial class kullanın:");
        sb.AppendLine($"    //");
        sb.AppendLine($"    // public partial class {entity.Name}Hooks");
        sb.AppendLine($"    // {{");
        sb.AppendLine($"    //     public Task BeforeCreate({entity.Name} entity, AppContext ctx)");
        sb.AppendLine($"    //     {{");
        sb.AppendLine($"    //         // özel mantık");
        sb.AppendLine($"    //         return Task.CompletedTask;");
        sb.AppendLine($"    //     }}");
        sb.AppendLine($"    // }}");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
```

```csharp
// ---- Üretilen UserHooks.g.cs ----
// <auto-generated />
using CrudKit.Core.Interfaces;
using CrudKit.Core.Context;
namespace MyApp.Entities;

public partial class UserHooks : ICrudHooks<User>
{
    // Hook'ları override etmek için partial class kullanın
}

// ---- Kullanıcının yazdığı UserHooks.cs (partial) ----
namespace MyApp.Entities;

public partial class UserHooks
{
    public Task BeforeCreate(User entity, AppContext ctx)
    {
        entity.PasswordHash = BCrypt.Net.BCrypt.HashPassword(entity.PasswordHash);
        entity.Role = "user";
        return Task.CompletedTask;
    }
}

// DI kayıt (bu da otomatik üretilebilir):
services.AddScoped<ICrudHooks<User>, UserHooks>();
```

### 12.9 Entity Mapper Generator — Sıfır Reflection

Handler'lar içinde DTO→Entity ve Entity→Update mapping'i şu an reflection ile yapılıyor. Her request'te `GetProperties()`, `GetCustomAttribute()` çağrılıyor. SourceGen bunu derleme sırasında birebir mapper koduna çevirir — sıfır reflection, sıfır runtime maliyeti.

#### IEntityMapper Interface

```csharp
// ---- CrudKit.Core/Interfaces/IEntityMapper.cs ----

/// <summary>
/// DTO ↔ Entity mapping. Source Generator tarafından implemente edilir.
/// Reflection yerine compile-time kod üretimi ile çalışır.
/// </summary>
public interface IEntityMapper<TEntity, TCreate, TUpdate>
    where TEntity : class, IEntity
    where TCreate : class
    where TUpdate : class
{
    /// <summary>CreateDto'dan yeni entity oluşturur.</summary>
    TEntity FromCreateDto(TCreate dto);

    /// <summary>UpdateDto'daki değişiklikleri entity'ye uygular. Optional.HasValue kontrolü yapar.</summary>
    void ApplyUpdate(TEntity entity, TUpdate dto);

    /// <summary>Response'a dönüştürülürken [SkipResponse] alanları temizler.</summary>
    void ClearSkippedFields(TEntity entity);
}
```

#### MapperGenerator

```csharp
// ---- CrudKit.SourceGen/Generators/MapperGenerator.cs ----

public static class MapperGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        var createProps = entity.Properties
            .Where(p => !p.IsSystemField)
            .ToList();

        var updateProps = entity.Properties
            .Where(p => !p.IsSystemField && !p.SkipUpdate && !p.IsProtected)
            .ToList();

        var skipResponseProps = entity.Properties
            .Where(p => p.SkipResponse)
            .ToList();

        var hashedProps = entity.Properties
            .Where(p => p.IsHashed)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine($"using CrudKit.Core.Interfaces;");
        sb.AppendLine($"using CrudKit.Core.Models;");
        sb.AppendLine($"namespace {entity.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public class {entity.Name}Mapper : IEntityMapper<{entity.Name}, Create{entity.Name}, Update{entity.Name}>");
        sb.AppendLine("{");

        // ---- FromCreateDto ----
        sb.AppendLine($"    public {entity.Name} FromCreateDto(Create{entity.Name} dto)");
        sb.AppendLine("    {");
        sb.AppendLine($"        return new {entity.Name}");
        sb.AppendLine("        {");
        foreach (var p in createProps)
        {
            sb.AppendLine($"            {p.Name} = dto.{p.Name},");
        }
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine();

        // ---- ApplyUpdate ----
        sb.AppendLine($"    public void ApplyUpdate({entity.Name} entity, Update{entity.Name} dto)");
        sb.AppendLine("    {");
        foreach (var p in updateProps)
        {
            sb.AppendLine($"        if (dto.{p.Name}.HasValue) entity.{p.Name} = dto.{p.Name}.Value;");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // ---- ClearSkippedFields ----
        sb.AppendLine($"    public void ClearSkippedFields({entity.Name} entity)");
        sb.AppendLine("    {");
        foreach (var p in skipResponseProps)
        {
            var defaultVal = p.IsNullable ? "null" : GetTypeDefault(p.Type);
            sb.AppendLine($"        entity.{p.Name} = {defaultVal};");
        }
        sb.AppendLine("    }");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GetTypeDefault(string type) => type switch
    {
        "string" => "\"\"",
        "int" or "long" or "decimal" or "double" or "float" => "0",
        "bool" => "false",
        _ => "default"
    };
}
```

#### Üretilen Mapper Örneği

```csharp
// ---- User.Mapper.g.cs — otomatik üretilir ----
// <auto-generated />
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;
namespace MyApp.Entities;

public class UserMapper : IEntityMapper<User, CreateUser, UpdateUser>
{
    public User FromCreateDto(CreateUser dto)
    {
        return new User
        {
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = dto.PasswordHash,
            Role = dto.Role,
            Age = dto.Age,
            IsActive = dto.IsActive,
        };
    }

    public void ApplyUpdate(User entity, UpdateUser dto)
    {
        if (dto.Username.HasValue) entity.Username = dto.Username.Value;
        if (dto.Email.HasValue) entity.Email = dto.Email.Value;
        // PasswordHash → [SkipUpdate] → burada yok
        if (dto.Role.HasValue) entity.Role = dto.Role.Value;
        if (dto.Age.HasValue) entity.Age = dto.Age.Value;
        if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;
        // Status → [Protected] → burada yok
    }

    public void ClearSkippedFields(User entity)
    {
        entity.PasswordHash = "";    // [SkipResponse] → response'da dönmez
    }
}
```

#### DI Kayıt — otomatik üretilen extension

```csharp
// ---- CrudKitMappers.g.cs — otomatik üretilir ----
// <auto-generated />
namespace CrudKit.Generated;

public static class CrudKitMapperExtensions
{
    /// <summary>
    /// Tüm entity mapper'ları DI'a register eder.
    /// </summary>
    public static IServiceCollection AddAllCrudMappers(this IServiceCollection services)
    {
        services.AddSingleton<IEntityMapper<User, CreateUser, UpdateUser>, UserMapper>();
        services.AddSingleton<IEntityMapper<Product, CreateProduct, UpdateProduct>, ProductMapper>();
        services.AddSingleton<IEntityMapper<Order, CreateOrder, UpdateOrder>, OrderMapper>();
        // ... 85 entity — otomatik
        return services;
    }
}
```

```csharp
// Program.cs — tek satır
builder.Services.AddAllCrudMappers();
```

#### EfRepo — Mapper kullanımı

```csharp
public class EfRepo<TContext, T> : IRepo<T>
    where TContext : CrudKitDbContext
    where T : class, IEntity
{
    private readonly TContext _db;
    private readonly IEntityMapper<T, object, object>? _mapper;  // DI'dan gelir

    // Create — reflection yok
    public async Task<T> Create(object createDto, CancellationToken ct = default)
    {
        T entity;

        if (_mapper != null)
        {
            // SourceGen mapper — hızlı, tip güvenli
            entity = (T)_mapper.GetType()
                .GetMethod("FromCreateDto")!
                .Invoke(_mapper, new[] { createDto })!;
        }
        else
        {
            // Fallback — reflection (SourceGen yoksa)
            entity = MapFromDtoReflection<T>(createDto);
        }

        _db.Set<T>().Add(entity);
        await _db.SaveChangesAsync(ct);

        // [SkipResponse] alanları temizle
        _mapper?.ClearSkippedFields(entity);

        return entity;
    }

    // Update — reflection yok
    public async Task<T> Update(string id, object updateDto, CancellationToken ct = default)
    {
        var entity = await _db.Set<T>().FindAsync(id, ct)
            ?? throw AppError.NotFound();

        if (_mapper != null)
        {
            _mapper.GetType()
                .GetMethod("ApplyUpdate")!
                .Invoke(_mapper, new object[] { entity, updateDto });
        }
        else
        {
            ApplyUpdateReflection(entity, updateDto);
        }

        await _db.SaveChangesAsync(ct);
        _mapper?.ClearSkippedFields(entity);

        return entity;
    }
}
```

#### Daha temiz alternatif — generic handler ile tip güvenli mapper

```csharp
// EfRepo'da object yerine generic mapper kullanımı:

public class EfRepo<TContext, TEntity, TCreate, TUpdate> : IRepo<TEntity>
    where TContext : CrudKitDbContext
    where TEntity : class, IEntity
    where TCreate : class
    where TUpdate : class
{
    private readonly IEntityMapper<TEntity, TCreate, TUpdate> _mapper;

    public async Task<TEntity> Create(TCreate createDto, CancellationToken ct = default)
    {
        var entity = _mapper.FromCreateDto(createDto);
        _db.Set<TEntity>().Add(entity);
        await _db.SaveChangesAsync(ct);
        _mapper.ClearSkippedFields(entity);
        return entity;
    }

    public async Task<TEntity> Update(string id, TUpdate updateDto, CancellationToken ct = default)
    {
        var entity = await _db.Set<TEntity>().FindAsync(id, ct)
            ?? throw AppError.NotFound();
        _mapper.ApplyUpdate(entity, updateDto);
        await _db.SaveChangesAsync(ct);
        _mapper.ClearSkippedFields(entity);
        return entity;
    }
}

// Bu yaklaşımda reflection sıfır — her şey compile-time.
// Ama IRepo interface'i de generic parametreleri taşımalı.
// Trade-off: daha fazla generic parametre vs sıfır reflection.
```

#### Testler

```csharp
// ---- MapperGeneratorTests.cs (CrudKit.SourceGen.Tests/Generators/) ----

public class MapperGeneratorTests
{
    [Fact]
    public async Task ShouldGenerateFromCreateDto()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                [Required]
                public string Username { get; set; } = "";
                public string Email { get; set; } = "";
                public int? Age { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var mapper = generated.First(g => g.Contains("UserMapper"));

        Assert.Contains("FromCreateDto(CreateUser dto)", mapper);
        Assert.Contains("Username = dto.Username", mapper);
        Assert.Contains("Email = dto.Email", mapper);
        Assert.Contains("Age = dto.Age", mapper);
        // System alanlar olmamalı
        Assert.DoesNotContain("Id = dto.Id", mapper);
        Assert.DoesNotContain("CreatedAt", mapper);
    }

    [Fact]
    public async Task ShouldGenerateApplyUpdate_WithOptionalCheck()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Username { get; set; } = "";
                [SkipUpdate]
                public string PasswordHash { get; set; } = "";
                [Protected]
                public string Status { get; set; } = "";
                public int? Age { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var mapper = generated.First(g => g.Contains("UserMapper"));

        Assert.Contains("if (dto.Username.HasValue) entity.Username = dto.Username.Value", mapper);
        Assert.Contains("if (dto.Age.HasValue) entity.Age = dto.Age.Value", mapper);
        // SkipUpdate ve Protected olmamalı
        Assert.DoesNotContain("PasswordHash", mapper);
        Assert.DoesNotContain("Status", mapper);
    }

    [Fact]
    public async Task ShouldGenerateClearSkippedFields()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Username { get; set; } = "";
                [SkipResponse]
                public string PasswordHash { get; set; } = "";
                [SkipResponse]
                public string SecurityStamp { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var mapper = generated.First(g => g.Contains("UserMapper"));

        Assert.Contains("entity.PasswordHash = \"\"", mapper);
        Assert.Contains("entity.SecurityStamp = \"\"", mapper);
        Assert.DoesNotContain("entity.Username", mapper.Split("ClearSkippedFields")[1]);
    }

    [Fact]
    public async Task ShouldGenerateMapperDiRegistration()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }

            [CrudEntity(Table = "products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = "";
                public string Title { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var diReg = generated.First(g => g.Contains("AddAllCrudMappers"));

        Assert.Contains("IEntityMapper<User, CreateUser, UpdateUser>, UserMapper", diReg);
        Assert.Contains("IEntityMapper<Product, CreateProduct, UpdateProduct>, ProductMapper", diReg);
    }
}
```

### 12.10 SourceGen ile SourceGen'siz Karşılaştırma

```
                            SourceGen ile              SourceGen'siz (elle)
──────────────────────────────────────────────────────────────────────────
CreateUser record           Otomatik üretilir          Elle yazılır
UpdateUser record           Optional<T> ile üretilir   Elle yazılır
UserResponse record         [SkipResponse] hariç       Elle yazılır veya unutulur
Entity mapper               Compile-time, 0 reflection Runtime reflection
Endpoint mapping            MapAllCrudEndpoints()      85 satır MapCrudEndpoints
Mapper DI kayıt             AddAllCrudMappers()        85 satır AddSingleton
Hook stub                   Partial class üretilir     Elle yazılır
Validation attribute        Entity'den kopyalanır      Elle tekrar yazılır
Derleme hatası              CRUD001-010 diagnostics    Runtime'da patlar

85 entity'de tahmini:
  Elle yazılan satır        ~200 (hook'lar)            ~8000+ (DTO + response + mapper + mapping)
  Bakım riski               Düşük                      Yüksek (entity değişince DTO/response unutulur)
  Runtime reflection        Sıfır                      Her request'te
  Data leak riski            Sıfır (compile-time)       Yüksek (ClearSkippedFields unutulur)
```

### 12.11 Diagnostics — Derleme Hatası ve Uyarıları

```csharp
// Source Generator derleme sırasında hata/uyarı üretebilir:

// CRUD001: Entity IEntity implemente etmiyor
// [CrudEntity(Table = "users")]
// public class User { }  // ← IEntity yok → derleme hatası

// CRUD002: SoftDelete açık ama ISoftDeletable implemente edilmemiş
// [CrudEntity(Table = "users", SoftDelete = true)]
// public class User : IEntity { }  // ← ISoftDeletable yok → uyarı

// CRUD003: MultiTenant açık ama IMultiTenant implemente edilmemiş

// CRUD004: [Unique] attribute var ama property entity'de yok

// CRUD005: Workflow açık ama workflow_protected alanları entity'de yok

// CRUD010: Table adı boş
// [CrudEntity(Table = "")]  → derleme hatası

public static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MissingIEntity = new(
        id: "CRUD001",
        title: "Entity must implement IEntity",
        messageFormat: "'{0}' has [CrudEntity] but does not implement IEntity",
        category: "CrudKit",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingSoftDeletable = new(
        id: "CRUD002",
        title: "SoftDelete requires ISoftDeletable",
        messageFormat: "'{0}' has SoftDelete=true but does not implement ISoftDeletable",
        category: "CrudKit",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingMultiTenant = new(
        id: "CRUD003",
        title: "MultiTenant requires IMultiTenant",
        messageFormat: "'{0}' has MultiTenant=true but does not implement IMultiTenant",
        category: "CrudKit",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EmptyTableName = new(
        id: "CRUD010",
        title: "Table name is required",
        messageFormat: "'{0}' has empty Table name in [CrudEntity]",
        category: "CrudKit",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
```

### 12.12 Testler

```
CrudKit.SourceGen.Tests/
├── Generators/
│   ├── CreateDtoGeneratorTests.cs
│   ├── UpdateDtoGeneratorTests.cs
│   ├── ResponseDtoGeneratorTests.cs
│   ├── MapperGeneratorTests.cs
│   ├── EndpointMappingGeneratorTests.cs
│   └── HookStubGeneratorTests.cs
├── Diagnostics/
│   └── DiagnosticTests.cs
├── Helpers/
│   └── GeneratorTestHelper.cs
└── CrudKit.SourceGen.Tests.csproj
```

```csharp
// ---- GeneratorTestHelper ----
// Microsoft.CodeAnalysis.CSharp.Testing.XUnit kullanarak
// source generator'ı test ortamında çalıştırır.

public static class GeneratorTestHelper
{
    public static async Task<(ImmutableArray<Diagnostic> Diagnostics, string[] GeneratedSources)>
        RunGenerator(string sourceCode)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CrudEntityAttribute).Assembly.Location),
            // ... diğer referanslar
        };

        var compilation = CSharpCompilation.Create("TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new CrudKitSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedSources = runResult.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .ToArray();

        return (diagnostics, generatedSources);
    }
}
```

```csharp
// ---- CreateDtoGeneratorTests.cs ----

public class CreateDtoGeneratorTests
{
    [Fact]
    public async Task ShouldGenerateCreateDto()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                [Required, MaxLength(50)]
                public string Username { get; set; } = "";
                [Required]
                public string Email { get; set; } = "";
                public int? Age { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (diagnostics, generated) = await GeneratorTestHelper.RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var createDto = generated.First(g => g.Contains("CreateUser"));
        Assert.Contains("public record CreateUser", createDto);
        Assert.Contains("string Username", createDto);
        Assert.Contains("string Email", createDto);
        Assert.Contains("int? Age = null", createDto);
        // Id, CreatedAt, UpdatedAt olmamalı
        Assert.DoesNotContain("string Id", createDto);
        Assert.DoesNotContain("CreatedAt", createDto);
    }

    [Fact]
    public async Task ShouldPreserveValidationAttributes()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;
            using System.ComponentModel.DataAnnotations;

            namespace TestApp;

            [CrudEntity(Table = "products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = "";
                [Required, MaxLength(100)]
                public string Name { get; set; } = "";
                [Range(0, 999999)]
                public decimal Price { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var createDto = generated.First(g => g.Contains("CreateProduct"));

        Assert.Contains("[Required]", createDto);
        Assert.Contains("[MaxLength(100)]", createDto);
        Assert.Contains("[Range(0, 999999)]", createDto);
    }
}

// ---- UpdateDtoGeneratorTests.cs ----

public class UpdateDtoGeneratorTests
{
    [Fact]
    public async Task ShouldGenerateOptionalProperties()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Username { get; set; } = "";
                [SkipUpdate]
                public string PasswordHash { get; set; } = "";
                [Protected]
                public string Status { get; set; } = "";
                public int? Age { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var updateDto = generated.First(g => g.Contains("UpdateUser"));

        Assert.Contains("Optional<string?>", updateDto);
        Assert.Contains("Username", updateDto);
        Assert.Contains("Age", updateDto);
        // SkipUpdate ve Protected alanlar olmamalı
        Assert.DoesNotContain("PasswordHash", updateDto);
        Assert.DoesNotContain("Status", updateDto);
        // System alanlar olmamalı
        Assert.DoesNotContain("CreatedAt", updateDto);
    }
}

// ---- EndpointMappingGeneratorTests.cs ----

public class EndpointMappingGeneratorTests
{
    [Fact]
    public async Task ShouldGenerateMapAllCrudEndpoints()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }

            [CrudEntity(Table = "products")]
            public class Product : IEntity
            {
                public string Id { get; set; } = "";
                public string Title { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var endpoints = generated.First(g => g.Contains("MapAllCrudEndpoints"));

        Assert.Contains("MapCrudEndpoints<TestApp.User, CreateUser, UpdateUser>", endpoints);
        Assert.Contains("MapCrudEndpoints<TestApp.Product, CreateProduct, UpdateProduct>", endpoints);
    }
}

// ---- DiagnosticTests.cs ----

public class DiagnosticTests
{
    [Fact]
    public async Task ShouldReportError_WhenIEntityMissing()
    {
        var source = """
            using CrudKit.Core.Attributes;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User    // IEntity yok!
            {
                public string Id { get; set; } = "";
            }
            """;

        var (diagnostics, _) = await GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "CRUD001");
    }

    [Fact]
    public async Task ShouldReportWarning_WhenSoftDeleteWithoutInterface()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users", SoftDelete = true)]
            public class User : IEntity    // ISoftDeletable yok!
            {
                public string Id { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (diagnostics, _) = await GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "CRUD002");
    }

    [Fact]
    public async Task ShouldReportError_WhenTableNameEmpty()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (diagnostics, _) = await GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "CRUD010");
    }

    [Fact]
    public async Task ShouldNotReport_WhenEverythingCorrect()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users", SoftDelete = true)]
            public class User : IEntity, ISoftDeletable
            {
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }
            }
            """;

        var (diagnostics, _) = await GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
    }
}
```

### 12.13 NuGet Bağımlılıkları

```xml
<!-- CrudKit.SourceGen -->
<PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.*" PrivateAssets="all" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" PrivateAssets="all" />

<!-- CrudKit.SourceGen.Tests -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit" Version="1.*" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.*" />
```

### 11.8 Operation Kontrolü — ReadOnly ve Kısmi CRUD

Sorun: Her entity tam CRUD gerektirmez. Audit log sadece okunur, fatura değiştirilemez, sipariş silinemez. Endpoint'ler entity bazında açılıp kapatılabilmeli. DTO'lar gereksiz yere üretilmemeli.

#### CrudEntityAttribute — operation flag'ları

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
    public bool EnableBulkUpdate { get; set; } = false;
    public bool EnableBulkDelete { get; set; } = false;

    // Operation kontrolü
    /// <summary>
    /// true → sadece List + GetById. Create, Update, Delete endpoint'leri oluşturulmaz.
    /// EnableCreate/Update/Delete'i false'a set etmenin kısayolu.
    /// </summary>
    public bool ReadOnly { get; set; }

    /// <summary>POST endpoint'i oluşturulsun mu? ReadOnly=true ise otomatik false.</summary>
    public bool EnableCreate { get; set; } = true;

    /// <summary>PUT endpoint'i oluşturulsun mu? ReadOnly=true ise otomatik false.</summary>
    public bool EnableUpdate { get; set; } = true;

    /// <summary>DELETE endpoint'i oluşturulsun mu? ReadOnly=true ise otomatik false.</summary>
    public bool EnableDelete { get; set; } = true;

    // Computed — gerçek durumu döner
    public bool IsCreateEnabled => !ReadOnly && EnableCreate;
    public bool IsUpdateEnabled => !ReadOnly && EnableUpdate;
    public bool IsDeleteEnabled => !ReadOnly && EnableDelete;
}
```

#### Kullanım örnekleri

```csharp
// ---- Tam CRUD (varsayılan) ----
[CrudEntity(Table = "products")]
public class Product : IEntity { ... }
// GET    /api/products
// GET    /api/products/:id
// POST   /api/products
// PUT    /api/products/:id
// DELETE /api/products/:id

// ---- ReadOnly — sadece oku ----
[CrudEntity(Table = "currencies", ReadOnly = true)]
public class Currency : IEntity { ... }
// GET    /api/currencies
// GET    /api/currencies/:id
// Başka endpoint yok

[CrudEntity(Table = "audit_logs", ReadOnly = true)]
public class AuditLogView : IEntity { ... }
// GET    /api/audit-logs
// GET    /api/audit-logs/:id

// ---- Create + Read (update ve delete yok) ----
[CrudEntity(Table = "invoices", EnableUpdate = false, EnableDelete = false)]
public class Invoice : IEntity { ... }
// GET    /api/invoices
// GET    /api/invoices/:id
// POST   /api/invoices
// PUT ve DELETE yok — fatura değiştirilemez/silinemez

// ---- Create + Read + Update (delete yok) ----
[CrudEntity(Table = "orders", EnableDelete = false)]
public class Order : IEntity { ... }
// GET    /api/orders
// GET    /api/orders/:id
// POST   /api/orders
// PUT    /api/orders/:id
// DELETE yok — sipariş silinemez (soft delete ile iptal edilebilir ama endpoint'ten değil)

// ---- Read + Delete (create ve update yok) ----
[CrudEntity(Table = "temp_imports", EnableCreate = false, EnableUpdate = false)]
public class TempImport : IEntity { ... }
// GET    /api/temp-imports
// GET    /api/temp-imports/:id
// DELETE /api/temp-imports/:id
// Başka sistem tarafından oluşturuluyor, kullanıcı sadece görüp silebilir
```

#### CrudEndpointMapper — koşullu endpoint kaydı

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
    var crudAttr = typeof(TEntity).GetCustomAttribute<CrudEntityAttribute>();

    // Read — her zaman var
    group.MapGet("/", ListHandler<TEntity>);
    group.MapGet("/{id}", GetHandler<TEntity>);

    // Create — flag açıksa
    if (crudAttr?.IsCreateEnabled ?? true)
    {
        group.MapPost("/", CreateHandler<TEntity, TCreate>)
            .AddEndpointFilter<ValidationFilter<TCreate>>()
            .AddEndpointFilter<IdempotencyFilter>();
    }

    // Update — flag açıksa
    if (crudAttr?.IsUpdateEnabled ?? true)
    {
        group.MapPut("/{id}", UpdateHandler<TEntity, TUpdate>)
            .AddEndpointFilter<ValidationFilter<TUpdate>>()
            .AddEndpointFilter<IdempotencyFilter>();
    }

    // Delete — flag açıksa
    if (crudAttr?.IsDeleteEnabled ?? true)
    {
        group.MapDelete("/{id}", DeleteHandler<TEntity>)
            .AddEndpointFilter<IdempotencyFilter>();
    }

    // Soft delete restore — delete açıksa ve soft delete varsa
    if ((crudAttr?.IsDeleteEnabled ?? true) && typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
        group.MapPost("/{id}/restore", RestoreHandler<TEntity>);

    // Bulk — ilgili flag'lar açıksa
    if (crudAttr?.EnableBulkUpdate == true && (crudAttr?.IsUpdateEnabled ?? true))
        group.MapPut("/bulk", BulkUpdateHandler<TEntity>).RequireAuth();

    if (crudAttr?.EnableBulkDelete == true && (crudAttr?.IsDeleteEnabled ?? true))
    {
        group.MapDelete("/bulk", BulkDeleteHandler<TEntity>).RequireAuth();
        group.MapPost("/bulk/count", BulkCountHandler<TEntity>).RequireAuth();
    }

    // Workflow — create açıksa
    if (crudAttr?.Workflow != null && (crudAttr?.IsCreateEnabled ?? true))
    {
        group.MapGet("/{id}/workflow", WorkflowStatusHandler<TEntity>);
        group.MapPost("/{id}/workflow/approve/{stepId}", WorkflowApproveHandler<TEntity>);
        group.MapPost("/{id}/workflow/reject/{stepId}", WorkflowRejectHandler<TEntity>);
        group.MapGet("/{id}/workflow/history", WorkflowHistoryHandler<TEntity>);
        group.MapPost("/{id}/workflow/cancel", WorkflowCancelHandler<TEntity>);
    }

    return group;
}
```

#### ReadOnly entity için overload — DTO parametresi gerektirmez

```csharp
// ReadOnly entity'ler için Create ve Update DTO gereksiz
// Ayrı bir overload ile sadece entity tipi yeterli

public static RouteGroupBuilder MapCrudEndpoints<TEntity>(
    this WebApplication app,
    string route)
    where TEntity : class, IEntity
{
    var group = app.MapGroup($"/api/{route}");

    // Sadece read
    group.MapGet("/", ListHandler<TEntity>);
    group.MapGet("/{id}", GetHandler<TEntity>);

    return group;
}

// Kullanım:
app.MapCrudEndpoints<Currency>("currencies");           // ReadOnly — DTO yok
app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products");  // Tam CRUD
```

#### SourceGen — koşullu DTO üretimi

```csharp
// SourceGen entity'nin flag'larına göre DTO üretip üretmeyeceğine karar verir

// CreateDtoGenerator:
public static string? Generate(EntityMetadata entity)
{
    // ReadOnly veya EnableCreate=false → CreateDto üretme
    if (entity.ReadOnly || !entity.EnableCreate) return null;
    // ... mevcut üretim kodu
}

// UpdateDtoGenerator:
public static string? Generate(EntityMetadata entity)
{
    // ReadOnly veya EnableUpdate=false → UpdateDto üretme
    if (entity.ReadOnly || !entity.EnableUpdate) return null;
    // ... mevcut üretim kodu
}

// MapperGenerator:
public static string? Generate(EntityMetadata entity)
{
    // ReadOnly → mapper üretme (mapping yok çünkü create/update yok)
    if (entity.ReadOnly) return null;

    // Kısmi CRUD → sadece var olan operasyonlar için mapper üret
    // EnableCreate=false → FromCreateDto metodu olmaz
    // EnableUpdate=false → ApplyUpdate metodu olmaz
    // ...
}
```

```csharp
// ---- Üretilen EndpointMapping — ReadOnly entity DTO gerektirmez ----
// <auto-generated />
public static WebApplication MapAllCrudEndpoints(this WebApplication app)
{
    // Tam CRUD
    app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct>("products");
    app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder>("orders");

    // Kısmi CRUD
    app.MapCrudEndpoints<Invoice, CreateInvoice, UpdateInvoice>("invoices");
    // EnableUpdate=false, EnableDelete=false ama DTO parametresi var
    // CrudEndpointMapper içinde flag kontrolü yapılıyor

    // ReadOnly — DTO parametresi yok
    app.MapCrudEndpoints<Currency>("currencies");
    app.MapCrudEndpoints<AuditLogView>("audit-logs");

    return app;
}
```

#### Schema endpoint — operation bilgisi

```csharp
// GET /api/_schema response'unda hangi operasyonların açık olduğu belirtilir

// {
//   "entities": [
//     {
//       "name": "Currency",
//       "table": "currencies",
//       "endpoint": "/api/currencies",
//       "operations": {
//         "list": true,
//         "get": true,
//         "create": false,
//         "update": false,
//         "delete": false,
//         "bulkUpdate": false,
//         "bulkDelete": false
//       },
//       "features": {
//         "readOnly": true,
//         "softDelete": false,
//         ...
//       }
//     }
//   ]
// }
//
// Frontend bu bilgiyle:
// - ReadOnly entity için "Ekle" butonu göstermez
// - EnableDelete=false ise "Sil" butonu göstermez
// - EnableUpdate=false ise satıra tıklanabilirlik kaldırılır
```

#### Diagnostics — SourceGen uyarıları

```csharp
// CRUD006: ReadOnly entity için ICrudHooks implemente edilmiş — anlamsız
// [CrudEntity(Table = "currencies", ReadOnly = true)]
// public class Currency : IEntity { }
// public class CurrencyHooks : ICrudHooks<Currency>
//   → BeforeCreate asla çağrılmaz — uyarı ver

// CRUD007: ReadOnly entity'de SoftDelete açık — anlamsız
// [CrudEntity(Table = "logs", ReadOnly = true, SoftDelete = true)]
//   → Delete endpoint yok, soft delete anlamsız — uyarı ver

// CRUD008: ReadOnly entity'de Workflow tanımlı — anlamsız
// [CrudEntity(Table = "reports", ReadOnly = true, Workflow = "report_flow")]
//   → Create yok, workflow başlatılamaz — uyarı ver

public static readonly DiagnosticDescriptor ReadOnlyWithHooks = new(
    id: "CRUD006",
    title: "ReadOnly entity has hooks",
    messageFormat: "'{0}' is ReadOnly but has ICrudHooks — create/update/delete hooks will never be called",
    category: "CrudKit",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor ReadOnlyWithSoftDelete = new(
    id: "CRUD007",
    title: "ReadOnly entity has SoftDelete",
    messageFormat: "'{0}' is ReadOnly with SoftDelete=true — delete endpoint does not exist",
    category: "CrudKit",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor ReadOnlyWithWorkflow = new(
    id: "CRUD008",
    title: "ReadOnly entity has Workflow",
    messageFormat: "'{0}' is ReadOnly with Workflow — create endpoint does not exist, workflow cannot start",
    category: "CrudKit",
    defaultSeverity: DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

#### Testler

```csharp
// ---- OperationControlTests.cs (CrudKit.Api.Tests/Endpoints/) ----

public class OperationControlTests : IClassFixture<ApiFixture>
{
    private readonly HttpClient _client;
    public OperationControlTests(ApiFixture f) => _client = f.Client;

    [Fact]
    public async Task ReadOnly_ShouldAllowGet()
    {
        var response = await _client.GetAsync("/api/currencies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnly_ShouldAllowGetById()
    {
        var response = await _client.GetAsync("/api/currencies/USD");
        // NotFound kabul edilebilir — önemli olan 405 olmaması
        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnly_ShouldReject_Post()
    {
        var response = await _client.PostAsJsonAsync("/api/currencies",
            new { Code = "TRY", Name = "Turkish Lira" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // 405 MethodNotAllowed değil 404 — endpoint hiç register edilmedi
    }

    [Fact]
    public async Task ReadOnly_ShouldReject_Put()
    {
        var response = await _client.PutAsJsonAsync("/api/currencies/USD",
            new { Name = "Changed" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReadOnly_ShouldReject_Delete()
    {
        var response = await _client.DeleteAsync("/api/currencies/USD");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EnableDeleteFalse_ShouldAllowCreateAndUpdate()
    {
        // Order: EnableDelete = false
        var createResponse = await _client.PostAsJsonAsync("/api/orders",
            new { Total = 100, Status = "new" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var order = await createResponse.Content.ReadFromJsonAsync<TestOrder>();

        var updateResponse = await _client.PutAsJsonAsync($"/api/orders/{order!.Id}",
            new { Status = "processing" });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
    }

    [Fact]
    public async Task EnableDeleteFalse_ShouldRejectDelete()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/orders",
            new { Total = 100, Status = "new" });
        var order = await createResponse.Content.ReadFromJsonAsync<TestOrder>();

        var deleteResponse = await _client.DeleteAsync($"/api/orders/{order!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task EnableUpdateFalse_ShouldRejectPut()
    {
        // Invoice: EnableUpdate = false, EnableDelete = false
        var createResponse = await _client.PostAsJsonAsync("/api/invoices",
            new { Total = 500, InvoiceNo = "FTR-001" });
        var invoice = await createResponse.Content.ReadFromJsonAsync<TestInvoice>();

        var updateResponse = await _client.PutAsJsonAsync($"/api/invoices/{invoice!.Id}",
            new { Total = 600 });
        Assert.Equal(HttpStatusCode.NotFound, updateResponse.StatusCode);
    }

    [Fact]
    public async Task Schema_ShouldShowOperations()
    {
        var response = await _client.GetAsync("/api/_schema");
        var schema = await response.Content.ReadFromJsonAsync<JsonElement>();

        var currency = schema.GetProperty("entities").EnumerateArray()
            .First(e => e.GetProperty("name").GetString() == "Currency");

        var ops = currency.GetProperty("operations");
        Assert.True(ops.GetProperty("list").GetBoolean());
        Assert.True(ops.GetProperty("get").GetBoolean());
        Assert.False(ops.GetProperty("create").GetBoolean());
        Assert.False(ops.GetProperty("update").GetBoolean());
        Assert.False(ops.GetProperty("delete").GetBoolean());
    }
}

// ---- SourceGen — DTO üretim testleri ----
// (CrudKit.SourceGen.Tests/Generators/)

public class ReadOnlyDtoTests
{
    [Fact]
    public async Task ReadOnlyEntity_ShouldNotGenerateCreateDto()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "currencies", ReadOnly = true)]
            public class Currency : IEntity
            {
                public string Id { get; set; } = "";
                public string Code { get; set; } = "";
                public string Name { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain(generated, g => g.Contains("CreateCurrency"));
        Assert.DoesNotContain(generated, g => g.Contains("UpdateCurrency"));
        Assert.DoesNotContain(generated, g => g.Contains("CurrencyMapper"));
    }

    [Fact]
    public async Task EnableUpdateFalse_ShouldNotGenerateUpdateDto()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "invoices", EnableUpdate = false, EnableDelete = false)]
            public class Invoice : IEntity
            {
                public string Id { get; set; } = "";
                public string InvoiceNo { get; set; } = "";
                public decimal Total { get; set; }
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);

        // CreateDto üretilmeli (EnableCreate = true)
        Assert.Contains(generated, g => g.Contains("CreateInvoice"));
        // UpdateDto üretilmemeli (EnableUpdate = false)
        Assert.DoesNotContain(generated, g => g.Contains("UpdateInvoice"));
    }

    [Fact]
    public async Task ReadOnlyEntity_ShouldGenerateSimpleEndpointMapping()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "currencies", ReadOnly = true)]
            public class Currency : IEntity
            {
                public string Id { get; set; } = "";
                public string Code { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var endpoints = generated.First(g => g.Contains("MapAllCrudEndpoints"));

        // ReadOnly → DTO parametresiz overload
        Assert.Contains("MapCrudEndpoints<TestApp.Currency>(\"currencies\")", endpoints);
        Assert.DoesNotContain("CreateCurrency", endpoints);
    }

    [Fact]
    public async Task ReadOnlyWithSoftDelete_ShouldWarn()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "logs", ReadOnly = true, SoftDelete = true)]
            public class Log : IEntity, ISoftDeletable
            {
                public string Id { get; set; } = "";
                public string Message { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
                public DateTime? DeletedAt { get; set; }
            }
            """;

        var (diagnostics, _) = await GeneratorTestHelper.RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "CRUD007");
    }
}
```

### 11.9 ResponseDto — Tip Güvenli Response

Sorun: `[SkipResponse]` alanları runtime'da `ClearSkippedFields()` ile temizlemek hack. Property entity'de hâlâ var, Swagger'da görünür, birisi temizlemeyi unutursa hassas veri leak eder. ResponseDto ile alan fiziksel olarak olmaz — compile-time garanti.

#### IEntityMapper güncelleme

```csharp
public interface IEntityMapper<TEntity, TCreate, TUpdate, TResponse>
    where TEntity : class, IEntity
    where TCreate : class
    where TUpdate : class
    where TResponse : class
{
    TEntity FromCreateDto(TCreate dto);
    void ApplyUpdate(TEntity entity, TUpdate dto);
    TResponse ToResponse(TEntity entity);
    List<TResponse> ToResponseList(IEnumerable<TEntity> entities);
}
```

#### ResponseDto Generator

```csharp
// ---- CrudKit.SourceGen/Generators/ResponseDtoGenerator.cs ----

public static class ResponseDtoGenerator
{
    public static string Generate(EntityMetadata entity)
    {
        // [SkipResponse] alanlar hariç
        // [Hashed] alanlar hariç (hash değerini response'da gösterme)
        // Geri kalan tüm property'ler dahil (system alanlar dahil: Id, CreatedAt, UpdatedAt)
        var props = entity.Properties
            .Where(p => !p.SkipResponse && !p.IsHashed)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine($"namespace {entity.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"public record {entity.Name}Response(");

        for (int i = 0; i < props.Count; i++)
        {
            var p = props[i];
            var comma = i < props.Count - 1 ? "," : "";
            var type = p.IsNullable ? $"{p.Type}?" : p.Type;
            sb.AppendLine($"    {type} {p.Name}{comma}");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }
}
```

#### Üretilen ResponseDto örneği

```csharp
// Entity:
[CrudEntity(Table = "users", SoftDelete = true)]
public class User : IEntity, ISoftDeletable
{
    public string Id { get; set; } = "";
    [Required, MaxLength(50), Searchable]
    public string Username { get; set; } = "";
    [Required, EmailAddress]
    public string Email { get; set; } = "";
    [Required, Hashed, SkipResponse]
    public string PasswordHash { get; set; } = "";
    [SkipResponse]
    public string SecurityStamp { get; set; } = "";
    public string Role { get; set; } = "user";
    public int? Age { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

// Üretilen UserResponse:
// <auto-generated />
namespace MyApp.Entities;

public record UserResponse(
    string Id,
    string Username,
    string Email,
    // PasswordHash → [Hashed] + [SkipResponse] → YOK
    // SecurityStamp → [SkipResponse] → YOK
    string Role,
    int? Age,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? DeletedAt
);
```

#### Mapper — ToResponse üretimi

```csharp
// Üretilen UserMapper güncellendi:

public class UserMapper : IEntityMapper<User, CreateUser, UpdateUser, UserResponse>
{
    public User FromCreateDto(CreateUser dto)
    {
        return new User
        {
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = dto.PasswordHash,
            Role = dto.Role,
            Age = dto.Age,
            IsActive = dto.IsActive,
        };
    }

    public void ApplyUpdate(User entity, UpdateUser dto)
    {
        if (dto.Username.HasValue) entity.Username = dto.Username.Value;
        if (dto.Email.HasValue) entity.Email = dto.Email.Value;
        if (dto.Role.HasValue) entity.Role = dto.Role.Value;
        if (dto.Age.HasValue) entity.Age = dto.Age.Value;
        if (dto.IsActive.HasValue) entity.IsActive = dto.IsActive.Value;
    }

    public UserResponse ToResponse(User entity)
    {
        return new UserResponse(
            Id: entity.Id,
            Username: entity.Username,
            Email: entity.Email,
            // PasswordHash → yok
            // SecurityStamp → yok
            Role: entity.Role,
            Age: entity.Age,
            IsActive: entity.IsActive,
            CreatedAt: entity.CreatedAt,
            UpdatedAt: entity.UpdatedAt,
            DeletedAt: entity.DeletedAt
        );
    }

    public List<UserResponse> ToResponseList(IEnumerable<User> entities)
    {
        return entities.Select(ToResponse).ToList();
    }

    // ClearSkippedFields artık gereksiz — kaldırıldı
}
```

#### Handler'lar — ResponseDto kullanımı

```csharp
// ---- Create handler ----
static async Task<IResult> CreateHandler<TEntity, TCreate, TResponse>(...)
{
    // ...
    var entity = mapper.FromCreateDto(body);
    db.Set<TEntity>().Add(entity);
    await db.SaveChangesAsync(ct);
    // ...

    // Entity değil ResponseDto dönüyor
    var response = mapper.ToResponse(entity);
    return Results.Created($".../{entity.Id}", response);
}

// ---- Get handler ----
static async Task<IResult> GetHandler<TEntity, TResponse>(...)
{
    var entity = await repo.FindById(id, ct);
    var response = mapper.ToResponse(entity);
    return Results.Ok(response);
}

// ---- List handler ----
static async Task<IResult> ListHandler<TEntity, TResponse>(...)
{
    var result = await repo.List(listParams, ct);
    var response = new Paginated<TResponse>
    {
        Data = mapper.ToResponseList(result.Data),
        Total = result.Total,
        Page = result.Page,
        PerPage = result.PerPage,
        TotalPages = result.TotalPages,
    };
    return Results.Ok(response);
}
```

#### Endpoint mapping güncelleme — 4 generic parametre

```csharp
// Tam CRUD — 4 tip parametresi
app.MapCrudEndpoints<User, CreateUser, UpdateUser, UserResponse>("users");

// ReadOnly — sadece entity + response
app.MapCrudEndpoints<Currency, CurrencyResponse>("currencies");

// SourceGen ürettiği:
public static WebApplication MapAllCrudEndpoints(this WebApplication app)
{
    // Tam CRUD
    app.MapCrudEndpoints<Product, CreateProduct, UpdateProduct, ProductResponse>("products");
    app.MapCrudEndpoints<Order, CreateOrder, UpdateOrder, OrderResponse>("orders");

    // Kısmi CRUD
    app.MapCrudEndpoints<Invoice, CreateInvoice, UpdateInvoice, InvoiceResponse>("invoices");

    // ReadOnly
    app.MapCrudEndpoints<Currency, CurrencyResponse>("currencies");
    app.MapCrudEndpoints<AuditLogView, AuditLogViewResponse>("audit-logs");

    return app;
}
```

#### [SkipResponse] olmayan entity — ResponseDto = Entity

```csharp
// Entity'de hiç [SkipResponse] veya [Hashed] alan yoksa
// ResponseDto üretmek gereksiz — entity'nin kendisi response olarak kullanılır

// SourceGen bu kontrolü yapar:
public static string? Generate(EntityMetadata entity)
{
    var skippedCount = entity.Properties
        .Count(p => p.SkipResponse || p.IsHashed);

    // Hiç skip/hashed alan yoksa → ResponseDto üretme
    if (skippedCount == 0) return null;

    // ... üretim kodu
}

// Mapper'da:
// ResponseDto yoksa entity'nin kendisini dön
public class CategoryMapper : IEntityMapper<Category, CreateCategory, UpdateCategory, Category>
{
    // ToResponse → identity mapping
    public Category ToResponse(Category entity) => entity;
    public List<Category> ToResponseList(IEnumerable<Category> entities) => entities.ToList();
}
```

#### Swagger / OpenAPI

```csharp
// ResponseDto ile Swagger temiz olur:
//
// POST /api/users → Request: CreateUser, Response: UserResponse
// GET  /api/users → Response: Paginated<UserResponse>
// PUT  /api/users/:id → Request: UpdateUser, Response: UserResponse
//
// UserResponse'da PasswordHash, SecurityStamp yok
// Swagger UI'da bu alanlar hiç görünmez
// Client SDK üretirken (NSwag, OpenAPI Generator) temiz tipler çıkar
```

#### Testler

```csharp
// ---- ResponseDtoGeneratorTests.cs (CrudKit.SourceGen.Tests/Generators/) ----

public class ResponseDtoGeneratorTests
{
    [Fact]
    public async Task ShouldExcludeSkipResponseFields()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Username { get; set; } = "";
                [SkipResponse]
                public string PasswordHash { get; set; } = "";
                [SkipResponse]
                public string SecurityStamp { get; set; } = "";
                public string Role { get; set; } = "user";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var responseDto = generated.First(g => g.Contains("UserResponse"));

        Assert.Contains("string Username", responseDto);
        Assert.Contains("string Role", responseDto);
        Assert.Contains("string Id", responseDto);
        Assert.DoesNotContain("PasswordHash", responseDto);
        Assert.DoesNotContain("SecurityStamp", responseDto);
    }

    [Fact]
    public async Task ShouldExcludeHashedFields()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Email { get; set; } = "";
                [Hashed]
                public string PasswordHash { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var responseDto = generated.First(g => g.Contains("UserResponse"));

        Assert.Contains("string Email", responseDto);
        Assert.DoesNotContain("PasswordHash", responseDto);
    }

    [Fact]
    public async Task ShouldNotGenerateResponseDto_WhenNoSkippedFields()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "categories")]
            public class Category : IEntity
            {
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
                public string Slug { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);

        // SkipResponse/Hashed alan yok → ResponseDto üretilmemeli
        Assert.DoesNotContain(generated, g => g.Contains("CategoryResponse"));
    }

    [Fact]
    public async Task MapperShouldIncludeToResponse()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Username { get; set; } = "";
                [SkipResponse]
                public string PasswordHash { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var mapper = generated.First(g => g.Contains("UserMapper"));

        Assert.Contains("ToResponse(User entity)", mapper);
        Assert.Contains("new UserResponse", mapper);
        Assert.Contains("Username: entity.Username", mapper);
        Assert.DoesNotContain("PasswordHash: entity.PasswordHash", mapper);
        // ClearSkippedFields olmamalı — artık gereksiz
        Assert.DoesNotContain("ClearSkippedFields", mapper);
    }

    [Fact]
    public async Task EndpointMapping_ShouldUseFourGenericParams()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "users")]
            public class User : IEntity
            {
                public string Id { get; set; } = "";
                public string Username { get; set; } = "";
                [SkipResponse]
                public string PasswordHash { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var endpoints = generated.First(g => g.Contains("MapAllCrudEndpoints"));

        Assert.Contains("MapCrudEndpoints<TestApp.User, CreateUser, UpdateUser, UserResponse>", endpoints);
    }

    [Fact]
    public async Task EntityWithoutSkippedFields_ShouldUseEntityAsResponse()
    {
        var source = """
            using CrudKit.Core.Attributes;
            using CrudKit.Core.Interfaces;

            namespace TestApp;

            [CrudEntity(Table = "categories")]
            public class Category : IEntity
            {
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
                public DateTime CreatedAt { get; set; }
                public DateTime UpdatedAt { get; set; }
            }
            """;

        var (_, generated) = await GeneratorTestHelper.RunGenerator(source);
        var endpoints = generated.First(g => g.Contains("MapAllCrudEndpoints"));

        // ResponseDto yok → entity'nin kendisi response: 3 generic parametre
        Assert.Contains("MapCrudEndpoints<TestApp.Category, CreateCategory, UpdateCategory>", endpoints);
        Assert.DoesNotContain("CategoryResponse", endpoints);
    }
}
```

### 11.10 Concurrency Handling — 409 Conflict

Sorun: `IConcurrent` ve `RowVersion` tanımlı ama handler'da `DbUpdateConcurrencyException` yakalanmıyor. İki kullanıcı aynı kaydı aynı anda güncellerse biri sessizce diğerini ezer.

#### Handler'da concurrency kontrolü

```csharp
// Update handler — transaction bloğuna eklenen catch

static async Task<IResult> UpdateHandler<TEntity, TUpdate, TResponse>(...)
{
    await using var transaction = await db.Database.BeginTransactionAsync(ct);

    try
    {
        var entity = await repo.FindById(id, ct);

        if (hooks != null) await hooks.BeforeUpdate(entity, appCtx);
        mapper.ApplyUpdate(entity, body);
        await db.SaveChangesAsync(ct);
        if (hooks != null) await hooks.AfterUpdate(entity, appCtx);

        await transaction.CommitAsync(ct);
        return Results.Ok(mapper.ToResponse(entity));
    }
    catch (DbUpdateConcurrencyException)
    {
        await transaction.RollbackAsync(ct);

        // Güncel veriyi getir ve client'a gönder
        var current = await repo.FindById(id, ct);
        return Results.Problem(
            statusCode: 409,
            title: "Conflict",
            detail: "Bu kayıt başka bir kullanıcı tarafından değiştirilmiş.",
            extensions: new Dictionary<string, object?>
            {
                ["currentData"] = mapper.ToResponse(current)
            });
    }
    catch
    {
        await transaction.RollbackAsync(ct);
        throw;
    }
}
```

#### Client tarafı

```
PUT /api/products/123
If-Match: "rowversion-value"      ← opsiyonel, header ile de gönderilebilir
{ "price": 200, "rowVersion": 5 }

→ 200 OK (başarılı)
→ 409 Conflict + { "currentData": { ... güncel veri ... } }
   Client güncel veriyi gösterir, kullanıcı tekrar dener
```

#### Testler

```csharp
public class ConcurrencyTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SimultaneousUpdate_ShouldReturn409()
    {
        // Kayıt oluştur
        var product = await CreateProduct();

        // İki paralel update — biri başarılı, diğeri 409
        var task1 = _client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { Price = 100, RowVersion = product.RowVersion });
        var task2 = _client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { Price = 200, RowVersion = product.RowVersion });

        var results = await Task.WhenAll(task1, task2);

        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(results, r => r.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConflictResponse_ShouldContainCurrentData()
    {
        var product = await CreateProduct();

        // İlk update başarılı
        await _client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { Price = 100, RowVersion = product.RowVersion });

        // İkinci update eski rowVersion ile — conflict
        var response = await _client.PutAsJsonAsync($"/api/products/{product.Id}",
            new { Price = 200, RowVersion = product.RowVersion });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("currentData", out _));
    }
}
```

### 11.11 Error Handling — AppErrorFilter

Sorun: `AppError` exception'ları ve hook'lardan fırlatılan hatalar HTTP response'a nasıl dönüşüyor net değil. Tüm hata dönüşüm mantığı tek yerde olmalı.

#### AppErrorFilter implementasyonu

```csharp
// ---- CrudKit.Api/Filters/AppErrorFilter.cs ----

public class AppErrorFilter : IMiddleware
{
    private readonly ILogger<AppErrorFilter> _logger;

    public AppErrorFilter(ILogger<AppErrorFilter> logger) => _logger = logger;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        try
        {
            await next(context);
        }
        catch (AppError ex)
        {
            // CrudKit hataları — beklenen, loglanmaz (veya warning)
            _logger.LogWarning(ex, "CrudKit error: {Code} {Message}", ex.Code, ex.Message);
            await WriteErrorResponse(context, ex.StatusCode, ex.Code, ex.Message, ex.Details);
        }
        catch (ValidationException ex)
        {
            // Validasyon hataları — DataAnnotation veya FluentValidation
            await WriteErrorResponse(context, 400, "VALIDATION_ERROR", ex.Message, ex.Value);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Concurrency — handler'da yakalanmadıysa buraya düşer
            _logger.LogWarning(ex, "Concurrency conflict");
            await WriteErrorResponse(context, 409, "CONFLICT",
                "Kayıt başka bir kullanıcı tarafından değiştirilmiş");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Unique constraint ihlali
            var field = ExtractFieldFromConstraint(ex);
            await WriteErrorResponse(context, 409, "DUPLICATE",
                $"'{field}' değeri zaten mevcut");
        }
        catch (OperationCanceledException)
        {
            // Client bağlantıyı kesti — normal, loglanmaz
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata — error log
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            // Production'da detay gösterme
            var detail = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
                ? ex.ToString()
                : "Beklenmeyen bir hata oluştu";

            await WriteErrorResponse(context, 500, "INTERNAL_ERROR", detail);
        }
    }

    private static async Task WriteErrorResponse(
        HttpContext context, int statusCode, string code, string message, object? details = null)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = $"https://crudkit.dev/errors/{code.ToLower()}",
            title = code,
            status = statusCode,
            detail = message,
            traceId = Activity.Current?.Id ?? context.TraceIdentifier,
            errors = details,
        };

        await context.Response.WriteAsJsonAsync(problem);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException?.Message ?? "";
        return inner.Contains("UNIQUE") || inner.Contains("duplicate key")
            || inner.Contains("IX_") || inner.Contains("unique constraint");
    }

    private static string ExtractFieldFromConstraint(DbUpdateException ex)
    {
        // Index adından alan adını çıkarmaya çalış: IX_users_email → email
        var inner = ex.InnerException?.Message ?? "";
        var match = System.Text.RegularExpressions.Regex.Match(inner, @"IX_\w+_(\w+)");
        return match.Success ? match.Groups[1].Value : "unknown";
    }
}
```

#### Response formatı — RFC 7807 Problem Details

```json
// 400 — Validasyon hatası
{
    "type": "https://crudkit.dev/errors/validation_error",
    "title": "VALIDATION_ERROR",
    "status": 400,
    "detail": "Validasyon hatası",
    "traceId": "00-abc123...",
    "errors": [
        { "field": "email", "code": "required", "message": "Email zorunludur" },
        { "field": "age", "code": "min", "message": "Yaş 0'dan büyük olmalı" }
    ]
}

// 404 — Kayıt bulunamadı
{
    "type": "https://crudkit.dev/errors/not_found",
    "title": "NOT_FOUND",
    "status": 404,
    "detail": "Kayıt bulunamadı",
    "traceId": "00-def456..."
}

// 409 — Conflict (unique veya concurrency)
{
    "type": "https://crudkit.dev/errors/duplicate",
    "title": "DUPLICATE",
    "status": 409,
    "detail": "'email' değeri zaten mevcut",
    "traceId": "00-ghi789..."
}

// 500 — Beklenmeyen hata (production)
{
    "type": "https://crudkit.dev/errors/internal_error",
    "title": "INTERNAL_ERROR",
    "status": 500,
    "detail": "Beklenmeyen bir hata oluştu",
    "traceId": "00-jkl012..."
}
```

#### Testler

```csharp
public class ErrorHandlingTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task NotFound_ShouldReturnProblemDetails()
    {
        var response = await _client.GetAsync("/api/products/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NOT_FOUND", body.GetProperty("title").GetString());
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task ValidationError_ShouldReturnFieldErrors()
    {
        var response = await _client.PostAsJsonAsync("/api/products",
            new { Name = "", Sku = "", Price = -10 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALIDATION_ERROR", body.GetProperty("title").GetString());
        Assert.True(body.GetProperty("errors").GetArrayLength() > 0);
    }

    [Fact]
    public async Task UniqueViolation_ShouldReturn409()
    {
        await _client.PostAsJsonAsync("/api/products",
            new { Name = "First", Sku = "DUP-001", Price = 10 });

        var response = await _client.PostAsJsonAsync("/api/products",
            new { Name = "Second", Sku = "DUP-001", Price = 20 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DUPLICATE", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task InternalError_ShouldNotLeakStackTrace_InProduction()
    {
        // Production modunda 500 hatası stack trace göstermemeli
        var response = await _client.GetAsync("/api/trigger-error");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain("at ", body.GetProperty("detail").GetString());
    }
}
```

### 11.12 OpenAPI / Swagger Entegrasyonu

Sorun: `EnableSwagger` flag'ı var ama SourceGen'in ürettiği DTO'larla Swagger nasıl entegre çalışır, endpoint metadata nasıl zenginleştirilir detayı yok.

#### AddCrudKitApi — Swagger konfigürasyonu

```csharp
public static IServiceCollection AddCrudKitApi(this IServiceCollection services, CrudKitApiOptions options)
{
    if (options.EnableSwagger)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = options.ApiTitle ?? "CrudKit API",
                Version = options.ApiVersion ?? "v1",
                Description = "Auto-generated CRUD API by CrudKit"
            });

            // Bearer token auth
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Authorization header: Bearer {token}"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // SourceGen ürettiği DTO'lar otomatik algılanır
            // record tipler Swagger'da doğru şekilde görünür
        });
    }

    return services;
}

public static WebApplication UseCrudKit(this WebApplication app)
{
    app.UseMiddleware<AppErrorFilter>();

    var options = app.Services.GetService<CrudKitApiOptions>();
    if (options?.EnableSwagger == true)
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", options.ApiTitle ?? "CrudKit API");
            c.RoutePrefix = "swagger";
        });
    }

    return app;
}
```

#### Endpoint metadata — WithTags, WithName, Produces

```csharp
// CrudEndpointMapper — endpoint'lere Swagger metadata ekler

public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate, TResponse>(
    this WebApplication app, string route, ...)
{
    var group = app.MapGroup($"/api/{route}")
        .WithTags(typeof(TEntity).Name);  // Swagger'da grupla

    if (crudAttr?.IsCreateEnabled ?? true)
    {
        group.MapPost("/", CreateHandler<TEntity, TCreate, TResponse>)
            .WithName($"Create{typeof(TEntity).Name}")
            .WithSummary($"Yeni {typeof(TEntity).Name} oluştur")
            .Accepts<TCreate>("application/json")
            .Produces<TResponse>(201)
            .Produces(400)
            .Produces(409)
            .AddEndpointFilter<ValidationFilter<TCreate>>()
            .AddEndpointFilter<IdempotencyFilter>();
    }

    group.MapGet("/", ListHandler<TEntity, TResponse>)
        .WithName($"List{typeof(TEntity).Name}")
        .WithSummary($"{typeof(TEntity).Name} listele")
        .Produces<Paginated<TResponse>>(200);

    group.MapGet("/{id}", GetHandler<TEntity, TResponse>)
        .WithName($"Get{typeof(TEntity).Name}")
        .WithSummary($"Tek {typeof(TEntity).Name} getir")
        .Produces<TResponse>(200)
        .Produces(404);

    if (crudAttr?.IsUpdateEnabled ?? true)
    {
        group.MapPut("/{id}", UpdateHandler<TEntity, TUpdate, TResponse>)
            .WithName($"Update{typeof(TEntity).Name}")
            .WithSummary($"{typeof(TEntity).Name} güncelle")
            .Accepts<TUpdate>("application/json")
            .Produces<TResponse>(200)
            .Produces(400)
            .Produces(404)
            .Produces(409);
    }

    if (crudAttr?.IsDeleteEnabled ?? true)
    {
        group.MapDelete("/{id}", DeleteHandler<TEntity>)
            .WithName($"Delete{typeof(TEntity).Name}")
            .WithSummary($"{typeof(TEntity).Name} sil")
            .Produces(200)
            .Produces(404);
    }

    return group;
}
```

#### CrudKitApiOptions — Swagger ayarları

```csharp
public class CrudKitApiOptions
{
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 100;
    public bool EnableSchemaEndpoint { get; set; } = true;
    public string ApiPrefix { get; set; } = "/api";

    // Swagger
    public bool EnableSwagger { get; set; } = true;
    public string? ApiTitle { get; set; }
    public string? ApiVersion { get; set; } = "v1";

    // Idempotency
    public bool EnableIdempotency { get; set; } = true;
    public TimeSpan IdempotencyKeyExpiry { get; set; } = TimeSpan.FromHours(24);
}
```

### 11.13 API Versioning

Sorun: API büyüdükçe breaking change'ler kaçınılmaz. Eski client'lar çalışmaya devam ederken yeni endpoint'ler eklenebilmeli.

#### Yaklaşım: URL prefix ile versiyonlama

```csharp
// CrudKit URL-based versioning destekler.
// Header-based veya query-based kullanıcının tercihine bırakılır.

// Konfigürasyon:
public class CrudKitApiOptions
{
    // ... mevcut alanlar ...

    /// <summary>
    /// API versiyon prefix'i. Null ise versiyonlama kapalı.
    /// Örn: "v1" → /api/v1/products
    /// </summary>
    public string? ApiVersion { get; set; } = null;
}
```

```csharp
// ---- CrudEndpointMapper — versiyon desteği ----

public static RouteGroupBuilder MapCrudEndpoints<TEntity, TCreate, TUpdate, TResponse>(
    this WebApplication app,
    string route,
    Action<CrudEndpointOptions>? configure = null)
{
    var options = app.Services.GetService<CrudKitApiOptions>();
    var prefix = options?.ApiPrefix ?? "/api";
    var version = options?.ApiVersion;

    // /api/products veya /api/v1/products
    var fullPrefix = version != null ? $"{prefix}/{version}" : prefix;
    var group = app.MapGroup($"{fullPrefix}/{route}");

    // ... endpoint kayıtları
    return group;
}
```

```csharp
// ---- Çoklu versiyon desteği ----

// Kullanıcı birden fazla versiyon çalıştırabilir:

var app = builder.Build();

// v1 — mevcut API
app.MapCrudEndpoints<User, CreateUserV1, UpdateUserV1, UserResponseV1>("users",
    opts => opts.Version = "v1");

// v2 — yeni alanlar eklendi, bazı alanlar kaldırıldı
app.MapCrudEndpoints<User, CreateUserV2, UpdateUserV2, UserResponseV2>("users",
    opts => opts.Version = "v2");

// Sonuç:
// GET /api/v1/users → UserResponseV1 döner
// GET /api/v2/users → UserResponseV2 döner
// Eski client'lar v1 kullanmaya devam eder
```

```csharp
// ---- SourceGen — versiyon farkındalığı ----

// SourceGen varsayılan DTO'ları üretir (versiyonsuz).
// Versiyonlu DTO'lar kullanıcı tarafından elle yazılır — çünkü
// hangi alanın hangi versiyonda olacağı iş kararı, otomatik üretilemez.
//
// Tipik senaryo:
// v1: SourceGen ürettiği CreateUser, UpdateUser, UserResponse kullanılır
// v2: Kullanıcı CreateUserV2, UserResponseV2 yazar
// v3: Kullanıcı CreateUserV3, UserResponseV3 yazar
// v1 SourceGen'den gelmeye devam eder

// SourceGen ürettiği MapAllCrudEndpoints versiyonsuz çalışır.
// Versiyonlu endpoint'ler elle yazılır.
```

```csharp
// ---- CrudEndpointOptions — endpoint bazlı konfigürasyon ----

public class CrudEndpointOptions
{
    public string? Version { get; set; }
    public bool RequireAuth { get; set; } = false;
    public string? RequireRole { get; set; }
    public string? Tag { get; set; }             // Swagger tag override
}
```

#### Testler

```csharp
public class ApiVersioningTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task VersionedEndpoint_ShouldWork()
    {
        var response = await _client.GetAsync("/api/v1/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DifferentVersions_ShouldCoexist()
    {
        var v1 = await _client.GetAsync("/api/v1/users");
        var v2 = await _client.GetAsync("/api/v2/users");

        Assert.Equal(HttpStatusCode.OK, v1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, v2.StatusCode);
    }

    [Fact]
    public async Task UnversionedEndpoint_ShouldWork_WhenVersionNull()
    {
        // ApiVersion = null → /api/products (versiyon yok)
        var response = await _client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

### 11.14 Structured Logging & Tracing

Sorun: CrudKit handler'larda ne olduğu loglanmıyor. Hata ayıklama zor. Hangi kullanıcı, hangi entity, hangi operasyon, ne kadar sürdü — bu bilgiler structured log olarak yazılmalı.

#### Request/Response logging middleware

```csharp
// ---- CrudKit.Api/Middleware/RequestLoggingMiddleware.cs ----

public class RequestLoggingMiddleware : IMiddleware
{
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        var requestId = Activity.Current?.Id ?? context.TraceIdentifier;

        // Structured log — request başlangıcı
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["UserId"] = context.Items["CurrentUserId"]?.ToString() ?? "anonymous",
            ["TenantId"] = context.Items["TenantId"]?.ToString() ?? "none",
            ["Method"] = context.Request.Method,
            ["Path"] = context.Request.Path.Value ?? "",
        }))
        {
            try
            {
                await next(context);

                sw.Stop();
                _logger.LogInformation(
                    "HTTP {Method} {Path} → {StatusCode} ({Duration}ms)",
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex,
                    "HTTP {Method} {Path} → FAILED ({Duration}ms)",
                    context.Request.Method,
                    context.Request.Path,
                    sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
```

#### Handler-level logging

```csharp
// CRUD handler'larının içinde otomatik loglama:

static async Task<IResult> CreateHandler<TEntity, TCreate, TResponse>(...)
{
    var logger = ctx.Services.GetRequiredService<ILogger<TEntity>>();
    var entityName = typeof(TEntity).Name;

    logger.LogInformation("Creating {Entity}", entityName);

    // ... create logic ...

    logger.LogInformation(
        "Created {Entity} {EntityId} by {UserId}",
        entityName, entity.Id, currentUser.Id);

    return Results.Created(...);
}

static async Task<IResult> UpdateHandler<TEntity, TUpdate, TResponse>(...)
{
    logger.LogInformation(
        "Updating {Entity} {EntityId} by {UserId}",
        entityName, id, currentUser.Id);

    // ... update logic ...

    logger.LogInformation(
        "Updated {Entity} {EntityId} — fields: {ChangedFields}",
        entityName, id, changedFields);
}

static async Task<IResult> DeleteHandler<TEntity>(...)
{
    logger.LogInformation(
        "Deleting {Entity} {EntityId} by {UserId} (soft: {IsSoftDelete})",
        entityName, id, currentUser.Id, typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)));
}
```

#### Konfigürasyon

```csharp
// Program.cs — structured logging setup

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "json";  // JSON structured log
});

// Veya Serilog:
builder.Host.UseSerilog((ctx, config) =>
{
    config
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "MyERP")
        .WriteTo.Console(new JsonFormatter())
        .WriteTo.Seq("http://localhost:5341");  // veya Elasticsearch, Datadog, vb.
});
```

#### UseCrudKit — middleware sırası

```csharp
public static WebApplication UseCrudKit(this WebApplication app)
{
    // 1. Request logging — en dışta, her şeyi sarar
    app.UseMiddleware<RequestLoggingMiddleware>();

    // 2. Error handling — exception'ları yakalar
    app.UseMiddleware<AppErrorFilter>();

    // 3. Swagger
    var options = app.Services.GetService<CrudKitApiOptions>();
    if (options?.EnableSwagger == true)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // 4. Development'ta auto-migrate
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CrudKitDbContext>();
        db.Database.Migrate();
    }

    return app;
}
```

#### Log çıktı formatı

```json
// Structured log örneği (JSON):
{
    "timestamp": "2026-04-03T14:30:00.123Z",
    "level": "Information",
    "message": "Created User usr-abc-123 by admin-1",
    "properties": {
        "RequestId": "00-abc123...",
        "UserId": "admin-1",
        "TenantId": "tenant-1",
        "Method": "POST",
        "Path": "/api/users",
        "Entity": "User",
        "EntityId": "usr-abc-123",
        "Duration": 45,
        "Application": "MyERP"
    }
}

// Hata logu:
{
    "timestamp": "2026-04-03T14:31:00.456Z",
    "level": "Error",
    "message": "HTTP POST /api/orders → FAILED (120ms)",
    "exception": "System.InvalidOperationException: Yetersiz stok...",
    "properties": {
        "RequestId": "00-def456...",
        "UserId": "user-5",
        "TenantId": "tenant-1",
        "Method": "POST",
        "Path": "/api/orders",
        "Duration": 120
    }
}
```

#### Health check endpoint

```csharp
// CrudKit otomatik health check endpoint'i ekler

public static WebApplication UseCrudKit(this WebApplication app)
{
    // ... mevcut middleware'ler ...

    // Health check
    app.MapGet("/health", async (CrudKitDbContext db) =>
    {
        try
        {
            await db.Database.CanConnectAsync();
            return Results.Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                checks = new
                {
                    database = "connected"
                }
            });
        }
        catch (Exception ex)
        {
            return Results.Json(new
            {
                status = "unhealthy",
                timestamp = DateTime.UtcNow,
                checks = new
                {
                    database = $"failed: {ex.Message}"
                }
            }, statusCode: 503);
        }
    }).WithTags("Health").ExcludeFromDescription();

    return app;
}
```

#### Testler

```csharp
public class LoggingTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CrudOperation_ShouldLogWithStructuredProperties()
    {
        // Test logger kullanarak logların doğru property'leri içerdiğini doğrula
        var logEntries = _testLogger.GetEntries();

        await _client.PostAsJsonAsync("/api/products",
            new { Name = "Logged", Sku = "LOG-001", Price = 10 });

        var createLog = logEntries.FirstOrDefault(l => l.Message.Contains("Created"));
        Assert.NotNull(createLog);
        Assert.Contains("Product", createLog.Properties["Entity"]?.ToString());
    }
}

public class HealthCheckTests : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task HealthEndpoint_ShouldReturnHealthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }
}
```
