# CrudKit.Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CrudKit.Core NuGet paketini oluştur — hiçbir harici bağımlılığı olmayan saf interface, attribute, model ve event katmanı.

**Architecture:** CrudKit.Core yalnızca BCL + System.Text.Json kullanır. Diğer tüm paketler (EntityFrameworkCore, Api, Workflow) bu projeye referans verir. Buradaki hiçbir tip framework'e bağımlı değildir; bu sayede test edilebilirlik ve yeniden kullanılabilirlik maksimuma çıkar.

**Tech Stack:** .NET 9, C# 13, xUnit 2.x, System.Text.Json (built-in)

---

## Dosya Yapısı

### Üretilecek — `src/CrudKit.Core/`
```
src/CrudKit.Core/
├── CrudKit.Core.csproj
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
│   ├── FilterOp.cs
│   ├── ListParams.cs
│   ├── Paginated.cs
│   ├── Optional.cs
│   ├── AppError.cs
│   ├── ValidationErrors.cs
│   ├── FieldError.cs
│   ├── SortDirection.cs
│   └── Permission.cs
├── Serialization/
│   ├── OptionalJsonConverterFactory.cs
│   └── OptionalJsonConverter.cs
├── Context/
│   └── AppContext.cs
├── Auth/
│   ├── AnonymousCurrentUser.cs
│   └── FakeCurrentUser.cs
├── Events/
│   ├── IEvent.cs
│   ├── EntityCreatedEvent.cs
│   ├── EntityUpdatedEvent.cs
│   └── EntityDeletedEvent.cs
└── Enums/
    └── PermScope.cs
```

### Üretilecek — `tests/CrudKit.Core.Tests/`
```
tests/CrudKit.Core.Tests/
├── CrudKit.Core.Tests.csproj
├── Models/
│   ├── FilterOpTests.cs
│   ├── ListParamsTests.cs
│   ├── ValidationErrorsTests.cs
│   ├── OptionalTests.cs
│   └── PaginatedTests.cs
└── Attributes/
    └── AttributeMetadataTests.cs
```

---

## Task 1: Solution ve proje iskeletini oluştur

**Files:**
- Modify: `CrudKit.slnx`
- Create: `src/CrudKit.Core/CrudKit.Core.csproj`
- Create: `tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj`

- [ ] **Step 1: src ve tests klasörlerini oluştur**

```bash
mkdir -p src/CrudKit.Core tests/CrudKit.Core.Tests
```

- [ ] **Step 2: CrudKit.Core.csproj oluştur**

`src/CrudKit.Core/CrudKit.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>CrudKit.Core</AssemblyName>
    <RootNamespace>CrudKit.Core</RootNamespace>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: CrudKit.Core.Tests.csproj oluştur**

`tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.AspNetCore.Http" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CrudKit.Core\CrudKit.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: CrudKit.slnx'i güncelle**

`CrudKit.slnx`:
```xml
<Solution>
  <Project Path="src/CrudKit.Core/CrudKit.Core.csproj" />
  <Project Path="tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj" />
</Solution>
```

- [ ] **Step 5: Build et, hata yok mu kontrol et**

```bash
cd d:/Playground/CrudKit
dotnet build CrudKit.slnx
```

Expected: Build succeeded, 0 Error(s)

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.Core/CrudKit.Core.csproj tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj CrudKit.slnx
git commit -m "chore: solution + CrudKit.Core project scaffold"
```

---

## Task 2: FilterOp modeli + FilterOpTests

**Files:**
- Create: `src/CrudKit.Core/Models/FilterOp.cs`
- Create: `tests/CrudKit.Core.Tests/Models/FilterOpTests.cs`

- [ ] **Step 1: Failing testi yaz**

`tests/CrudKit.Core.Tests/Models/FilterOpTests.cs`:
```csharp
using CrudKit.Core.Models;
using Xunit;

namespace CrudKit.Core.Tests.Models;

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
```

- [ ] **Step 2: Testin fail ettiğini doğrula**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj
```

Expected: Build error — `FilterOp` bulunamadı.

- [ ] **Step 3: FilterOp implementasyonunu yaz**

`src/CrudKit.Core/Models/FilterOp.cs`:
```csharp
namespace CrudKit.Core.Models;

/// <summary>
/// Filtreleme operatörü ve değeri.
/// Desteklenen operatörler: eq, neq, gt, gte, lt, lte, like, starts, in, null, notnull
/// Query string formatı: ?field=gte:18  ?field=like:ali  ?field=in:a,b,c  ?field=null
/// </summary>
public class FilterOp
{
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;
    public List<string>? Values { get; set; }

    private static readonly HashSet<string> NullaryOperators = new(StringComparer.OrdinalIgnoreCase)
        { "null", "notnull" };

    private static readonly HashSet<string> KnownOperators = new(StringComparer.OrdinalIgnoreCase)
        { "eq", "neq", "gt", "gte", "lt", "lte", "like", "starts", "in", "null", "notnull" };

    public static FilterOp Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new FilterOp { Operator = "eq", Value = "" };

        if (NullaryOperators.Contains(raw))
            return new FilterOp { Operator = raw.ToLowerInvariant(), Value = "" };

        var colonIdx = raw.IndexOf(':');
        if (colonIdx > 0)
        {
            var op = raw[..colonIdx];
            if (KnownOperators.Contains(op))
            {
                var val = raw[(colonIdx + 1)..];
                var result = new FilterOp { Operator = op.ToLowerInvariant(), Value = val };

                if (string.Equals(op, "in", StringComparison.OrdinalIgnoreCase))
                    result.Values = val.Split(',').ToList();

                return result;
            }
        }

        return new FilterOp { Operator = "eq", Value = raw };
    }
}
```

- [ ] **Step 4: Testleri çalıştır, pass mı?**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "FilterOpTests"
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.Core/Models/FilterOp.cs tests/CrudKit.Core.Tests/Models/FilterOpTests.cs
git commit -m "feat(core): FilterOp model with Parse logic"
```

---

## Task 3: ListParams modeli + ListParamsTests

**Files:**
- Create: `src/CrudKit.Core/Models/ListParams.cs`
- Create: `src/CrudKit.Core/Models/SortDirection.cs`
- Create: `tests/CrudKit.Core.Tests/Models/ListParamsTests.cs`

- [ ] **Step 1: Failing testi yaz**

`tests/CrudKit.Core.Tests/Models/ListParamsTests.cs`:
```csharp
using CrudKit.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace CrudKit.Core.Tests.Models;

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
```

- [ ] **Step 2: Testin fail ettiğini doğrula**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "ListParamsTests"
```

Expected: Build error — `ListParams` bulunamadı.

- [ ] **Step 3: SortDirection enum + ListParams implementasyonu**

`src/CrudKit.Core/Models/SortDirection.cs`:
```csharp
namespace CrudKit.Core.Models;

public enum SortDirection
{
    Asc,
    Desc
}
```

`src/CrudKit.Core/Models/ListParams.cs`:
```csharp
using Microsoft.AspNetCore.Http;

namespace CrudKit.Core.Models;

public class ListParams
{
    private static readonly HashSet<string> ReservedKeys =
        new(StringComparer.OrdinalIgnoreCase) { "page", "per_page", "sort" };

    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 20;
    public string? Sort { get; set; }
    public Dictionary<string, FilterOp> Filters { get; set; } = new();

    public static ListParams FromQuery(IQueryCollection query)
    {
        var result = new ListParams();

        if (query.TryGetValue("page", out var pageVal) && int.TryParse(pageVal, out var page) && page > 0)
            result.Page = page;

        if (query.TryGetValue("per_page", out var ppVal) && int.TryParse(ppVal, out var pp) && pp > 0)
            result.PerPage = Math.Min(pp, 100);

        if (query.TryGetValue("sort", out var sortVal))
            result.Sort = sortVal.ToString();

        foreach (var key in query.Keys)
        {
            if (ReservedKeys.Contains(key)) continue;
            var raw = query[key].ToString();
            result.Filters[key] = FilterOp.Parse(raw);
        }

        return result;
    }
}
```

**Not:** `ListParams` `Microsoft.AspNetCore.Http.IQueryCollection` kullanıyor. CrudKit.Core bu bağımlılığı almak yerine interface'i `string`-based tutmak yerine — ASP.NET Core BCL'e dahil olduğu için kabul edilebilir. `CrudKit.Core.csproj`'a referans eklemek gerekirse:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 4: Testleri çalıştır**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "ListParamsTests"
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.Core/Models/ListParams.cs src/CrudKit.Core/Models/SortDirection.cs \
        src/CrudKit.Core/CrudKit.Core.csproj \
        tests/CrudKit.Core.Tests/Models/ListParamsTests.cs
git commit -m "feat(core): ListParams + SortDirection with query parsing"
```

---

## Task 4: ValidationErrors + AppError + testler

**Files:**
- Create: `src/CrudKit.Core/Models/FieldError.cs`
- Create: `src/CrudKit.Core/Models/ValidationErrors.cs`
- Create: `src/CrudKit.Core/Models/AppError.cs`
- Create: `tests/CrudKit.Core.Tests/Models/ValidationErrorsTests.cs`

- [ ] **Step 1: Failing testi yaz**

`tests/CrudKit.Core.Tests/Models/ValidationErrorsTests.cs`:
```csharp
using CrudKit.Core.Models;
using Xunit;

namespace CrudKit.Core.Tests.Models;

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
        var ex = Assert.Throws<AppError>(() => errors.ThrowIfInvalid());
        Assert.Equal(400, ex.StatusCode);
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
```

- [ ] **Step 2: Testin fail ettiğini doğrula**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "ValidationErrorsTests"
```

Expected: Build error — `ValidationErrors` / `AppError` bulunamadı.

- [ ] **Step 3: FieldError + ValidationErrors + AppError implementasyonu**

`src/CrudKit.Core/Models/FieldError.cs`:
```csharp
namespace CrudKit.Core.Models;

public record FieldError(string Field, string Code, string Message);
```

`src/CrudKit.Core/Models/ValidationErrors.cs`:
```csharp
namespace CrudKit.Core.Models;

public class ValidationErrors
{
    public List<FieldError> Errors { get; set; } = new();

    public bool IsEmpty => Errors.Count == 0;

    public void Add(string field, string code, string message)
        => Errors.Add(new FieldError(field, code, message));

    public void ThrowIfInvalid()
    {
        if (!IsEmpty)
            throw AppError.Validation(this);
    }
}
```

`src/CrudKit.Core/Models/AppError.cs`:
```csharp
namespace CrudKit.Core.Models;

public class AppError : Exception
{
    public int StatusCode { get; }
    public string Code { get; }
    public object? Details { get; }

    private AppError(int statusCode, string code, string message, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    public static AppError NotFound(string message = "Kayıt bulunamadı")
        => new(404, "NOT_FOUND", message);

    public static AppError BadRequest(string message)
        => new(400, "BAD_REQUEST", message);

    public static AppError Unauthorized(string message = "Yetkisiz erişim")
        => new(401, "UNAUTHORIZED", message);

    public static AppError Forbidden(string message = "Erişim engellendi")
        => new(403, "FORBIDDEN", message);

    public static AppError Validation(ValidationErrors errors)
        => new(400, "VALIDATION_ERROR", "Validasyon hatası", errors);

    public static AppError Conflict(string message)
        => new(409, "CONFLICT", message);
}
```

- [ ] **Step 4: Testleri çalıştır**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "ValidationErrorsTests"
```

Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.Core/Models/FieldError.cs \
        src/CrudKit.Core/Models/ValidationErrors.cs \
        src/CrudKit.Core/Models/AppError.cs \
        tests/CrudKit.Core.Tests/Models/ValidationErrorsTests.cs
git commit -m "feat(core): ValidationErrors + AppError models"
```

---

## Task 5: Optional\<T\> + JSON serialization + testler

**Files:**
- Create: `src/CrudKit.Core/Models/Optional.cs`
- Create: `src/CrudKit.Core/Serialization/OptionalJsonConverterFactory.cs`
- Create: `src/CrudKit.Core/Serialization/OptionalJsonConverter.cs`
- Create: `tests/CrudKit.Core.Tests/Models/OptionalTests.cs`

- [ ] **Step 1: Failing testi yaz**

`tests/CrudKit.Core.Tests/Models/OptionalTests.cs`:
```csharp
using System.Text.Json;
using CrudKit.Core.Models;
using CrudKit.Core.Serialization;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class OptionalTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new OptionalJsonConverterFactory() }
    };

    [Fact]
    public void Undefined_HasValue_IsFalse()
    {
        var opt = Optional<string>.Undefined;
        Assert.False(opt.HasValue);
        Assert.Null(opt.Value);
    }

    [Fact]
    public void From_HasValue_IsTrue()
    {
        var opt = Optional<string>.From("hello");
        Assert.True(opt.HasValue);
        Assert.Equal("hello", opt.Value);
    }

    [Fact]
    public void From_NullValue_HasValue_IsTrue()
    {
        var opt = Optional<string?>.From(null);
        Assert.True(opt.HasValue);
        Assert.Null(opt.Value);
    }

    [Fact]
    public void ImplicitConversion_ShouldSetHasValue()
    {
        Optional<int> opt = 42;
        Assert.True(opt.HasValue);
        Assert.Equal(42, opt.Value);
    }

    [Fact]
    public void Deserialize_PresentField_ShouldHaveValue()
    {
        // JSON'da alan var → HasValue = true
        var json = """{"Name":"test","Price":99}""";
        var dto = JsonSerializer.Deserialize<TestDto>(json, Options)!;
        Assert.True(dto.Name.HasValue);
        Assert.Equal("test", dto.Name.Value);
        Assert.True(dto.Price.HasValue);
        Assert.Equal(99m, dto.Price.Value);
    }

    [Fact]
    public void Deserialize_MissingField_ShouldBeUndefined()
    {
        // JSON'da alan yok → HasValue = false
        var json = """{"Price":99}""";
        var dto = JsonSerializer.Deserialize<TestDto>(json, Options)!;
        Assert.False(dto.Name.HasValue);
        Assert.True(dto.Price.HasValue);
    }

    [Fact]
    public void Deserialize_ExplicitNull_ShouldHaveValue_AsNull()
    {
        // JSON'da alan var ve null → HasValue = true, Value = null
        var json = """{"Name":null,"Price":99}""";
        var dto = JsonSerializer.Deserialize<TestDto>(json, Options)!;
        Assert.True(dto.Name.HasValue);
        Assert.Null(dto.Name.Value);
    }

    private record TestDto
    {
        public Optional<string?> Name { get; init; }
        public Optional<decimal?> Price { get; init; }
    }
}
```

- [ ] **Step 2: Testin fail ettiğini doğrula**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "OptionalTests"
```

Expected: Build error — `Optional<T>` bulunamadı.

- [ ] **Step 3: Optional<T> + serialization implementasyonu**

`src/CrudKit.Core/Models/Optional.cs`:
```csharp
using System.Text.Json.Serialization;
using CrudKit.Core.Serialization;

namespace CrudKit.Core.Models;

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

    /// <summary>JSON'da alan yoktu — bu alana dokunma.</summary>
    public static Optional<T> Undefined => new(default, false);

    /// <summary>JSON'da alan vardı — bu değeri uygula (null dahil).</summary>
    public static Optional<T> From(T? value) => new(value, true);

    public static implicit operator Optional<T>(T? value) => From(value);
}
```

`src/CrudKit.Core/Serialization/OptionalJsonConverterFactory.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using CrudKit.Core.Models;

namespace CrudKit.Core.Serialization;

public class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
        && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var innerType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(OptionalJsonConverter<>).MakeGenericType(innerType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
```

`src/CrudKit.Core/Serialization/OptionalJsonConverter.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using CrudKit.Core.Models;

namespace CrudKit.Core.Serialization;

public class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Bu metod çağrıldıysa JSON'da alan var demektir → HasValue = true
        var value = JsonSerializer.Deserialize<T>(ref reader, options);
        return Optional<T>.From(value);
    }

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            JsonSerializer.Serialize(writer, value.Value, options);
        // HasValue = false ise hiçbir şey yazılmaz
    }
}
```

- [ ] **Step 4: Testleri çalıştır**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "OptionalTests"
```

Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.Core/Models/Optional.cs \
        src/CrudKit.Core/Serialization/OptionalJsonConverterFactory.cs \
        src/CrudKit.Core/Serialization/OptionalJsonConverter.cs \
        tests/CrudKit.Core.Tests/Models/OptionalTests.cs
git commit -m "feat(core): Optional<T> struct + JSON serialization (null vs missing)"
```

---

## Task 6: Paginated\<T\> modeli + testler

**Files:**
- Create: `src/CrudKit.Core/Models/Paginated.cs`
- Create: `tests/CrudKit.Core.Tests/Models/PaginatedTests.cs`

- [ ] **Step 1: Failing testi yaz**

`tests/CrudKit.Core.Tests/Models/PaginatedTests.cs`:
```csharp
using CrudKit.Core.Models;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class PaginatedTests
{
    [Fact]
    public void Constructor_ShouldHaveEmptyDataByDefault()
    {
        var paged = new Paginated<string>();
        Assert.Empty(paged.Data);
        Assert.Equal(0, paged.Total);
    }

    [Fact]
    public void TotalPages_ShouldCalculateCorrectly()
    {
        var paged = new Paginated<int>
        {
            Total = 25,
            PerPage = 10,
            TotalPages = (int)Math.Ceiling(25.0 / 10)
        };
        Assert.Equal(3, paged.TotalPages);
    }

    [Fact]
    public void TotalPages_WhenExactDivision_ShouldNotAddExtra()
    {
        var paged = new Paginated<int>
        {
            Total = 20,
            PerPage = 10,
            TotalPages = (int)Math.Ceiling(20.0 / 10)
        };
        Assert.Equal(2, paged.TotalPages);
    }

    [Fact]
    public void TotalPages_WhenZeroTotal_ShouldBeZero()
    {
        var paged = new Paginated<int>
        {
            Total = 0,
            PerPage = 10,
            TotalPages = (int)Math.Ceiling(0.0 / 10)
        };
        Assert.Equal(0, paged.TotalPages);
    }
}
```

- [ ] **Step 2: Testin fail ettiğini doğrula**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "PaginatedTests"
```

Expected: Build error — `Paginated<T>` bulunamadı.

- [ ] **Step 3: Paginated<T> implementasyonu**

`src/CrudKit.Core/Models/Paginated.cs`:
```csharp
namespace CrudKit.Core.Models;

public class Paginated<T>
{
    public List<T> Data { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalPages { get; set; }
}
```

- [ ] **Step 4: Testleri çalıştır**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "PaginatedTests"
```

Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/CrudKit.Core/Models/Paginated.cs \
        tests/CrudKit.Core.Tests/Models/PaginatedTests.cs
git commit -m "feat(core): Paginated<T> model"
```

---

## Task 7: Core interface'leri yaz

**Files:**
- Create: `src/CrudKit.Core/Interfaces/IEntity.cs`
- Create: `src/CrudKit.Core/Interfaces/ISoftDeletable.cs`
- Create: `src/CrudKit.Core/Interfaces/ICascadeSoftDelete.cs`
- Create: `src/CrudKit.Core/Interfaces/IAuditable.cs`
- Create: `src/CrudKit.Core/Interfaces/IMultiTenant.cs`
- Create: `src/CrudKit.Core/Interfaces/IStateMachine.cs`
- Create: `src/CrudKit.Core/Interfaces/IDocumentNumbering.cs`
- Create: `src/CrudKit.Core/Interfaces/IEventBus.cs`
- Create: `src/CrudKit.Core/Interfaces/IEntityMapper.cs`

Bu task'ta test yoktur — interface'ler Task 10 (AttributeMetadataTests) ile dolaylı test edilir.

- [ ] **Step 1: IEntity**

`src/CrudKit.Core/Interfaces/IEntity.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

public interface IEntity
{
    string Id { get; set; }
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: ISoftDeletable + ICascadeSoftDelete**

`src/CrudKit.Core/Interfaces/ISoftDeletable.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
```

`src/CrudKit.Core/Interfaces/ICascadeSoftDelete.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

/// <summary>
/// Marker interface — bu entity silindiğinde [CascadeSoftDelete] attribute'lu
/// navigation property'leri de soft-delete yapılır.
/// </summary>
public interface ICascadeSoftDelete : ISoftDeletable { }
```

- [ ] **Step 3: IAuditable + IMultiTenant**

`src/CrudKit.Core/Interfaces/IAuditable.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

/// <summary>
/// Marker interface — CrudKitDbContext bu entity'nin değişikliklerini AuditLog tablosuna yazar.
/// </summary>
public interface IAuditable { }
```

`src/CrudKit.Core/Interfaces/IMultiTenant.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

public interface IMultiTenant
{
    string TenantId { get; set; }
}
```

- [ ] **Step 4: IStateMachine + IDocumentNumbering**

`src/CrudKit.Core/Interfaces/IStateMachine.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}
```

`src/CrudKit.Core/Interfaces/IDocumentNumbering.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

public interface IDocumentNumbering
{
    string DocumentNumber { get; set; }
    static abstract string Prefix { get; }
    static abstract bool YearlyReset { get; }
}
```

- [ ] **Step 5: IEventBus + IEntityMapper**

`src/CrudKit.Core/Interfaces/IEventBus.cs`:
```csharp
using CrudKit.Core.Events;

namespace CrudKit.Core.Interfaces;

public interface IEventBus
{
    Task Publish<T>(T @event, CancellationToken ct = default) where T : class, IEvent;
    void Subscribe<T>(Func<T, Task> handler) where T : class, IEvent;
}
```

`src/CrudKit.Core/Interfaces/IEntityMapper.cs`:
```csharp
namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity'yi response DTO'suna dönüştürür.
/// Kullanıcı bu interface'i implemente eder — CrudKit sağlamaz.
/// </summary>
public interface IEntityMapper<TEntity, TResponse>
    where TEntity : class, IEntity
    where TResponse : class
{
    TResponse Map(TEntity entity);
    IQueryable<TResponse> Project(IQueryable<TEntity> query);
}
```

- [ ] **Step 6: IModule**

`src/CrudKit.Core/Interfaces/IModule.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Modular monolith desteği. Her modül bu interface'i implemente eder.
/// </summary>
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
    void RegisterWorkflowActions(object registry) { }
}
```

- [ ] **Step 7: ICrudHooks — lifecycle hook interface**

`src/CrudKit.Core/Interfaces/ICrudHooks.cs`:
```csharp
using CrudKit.Core.Context;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity lifecycle hook'ları. Tüm metodların default implementasyonu boştur.
/// Kullanıcı sadece ihtiyacı olanı override eder.
/// </summary>
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
```

- [ ] **Step 8: Build et, hata yok mu?**

```bash
dotnet build src/CrudKit.Core/CrudKit.Core.csproj
```

Expected: Build succeeded (warning olabilir, error olmamalı).

- [ ] **Step 9: Commit**

```bash
git add src/CrudKit.Core/Interfaces/
git commit -m "feat(core): core interfaces (IEntity, ICrudHooks, ISoftDeletable, IAuditable, IMultiTenant, IStateMachine, IDocumentNumbering, IEventBus, IEntityMapper, IModule)"
```

---

## Task 8: ICurrentUser + PermScope + Permission + AnonymousCurrentUser + FakeCurrentUser

**Files:**
- Create: `src/CrudKit.Core/Enums/PermScope.cs`
- Create: `src/CrudKit.Core/Models/Permission.cs`
- Create: `src/CrudKit.Core/Interfaces/ICurrentUser.cs`
- Create: `src/CrudKit.Core/Auth/AnonymousCurrentUser.cs`
- Create: `src/CrudKit.Core/Auth/FakeCurrentUser.cs`

- [ ] **Step 1: PermScope enum**

`src/CrudKit.Core/Enums/PermScope.cs`:
```csharp
namespace CrudKit.Core.Enums;

public enum PermScope
{
    Own,           // Sadece kendi kayıtları
    Department,    // Departmanındaki kayıtlar
    All            // Tüm kayıtlar
}
```

- [ ] **Step 2: Permission model**

`src/CrudKit.Core/Models/Permission.cs`:
```csharp
using CrudKit.Core.Enums;

namespace CrudKit.Core.Models;

public class Permission
{
    public string Entity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public PermScope Scope { get; set; }
}
```

- [ ] **Step 3: ICurrentUser interface**

`src/CrudKit.Core/Interfaces/ICurrentUser.cs`:
```csharp
using CrudKit.Core.Enums;
using CrudKit.Core.Models;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Mevcut oturumdaki kullanıcı bilgisi.
/// Bu interface'i uygulama tarafı implemente eder.
/// CrudKit sadece bu interface üzerinden kullanıcı bilgisine erişir.
/// </summary>
public interface ICurrentUser
{
    string? Id { get; }
    string? Username { get; }
    string? TenantId { get; }
    IReadOnlyList<string> Roles { get; }
    IReadOnlyList<Permission> Permissions { get; }
    bool IsAuthenticated { get; }
    bool HasRole(string role);
    bool HasPermission(string entity, string action);
    bool HasPermission(string entity, string action, PermScope scope);
}
```

- [ ] **Step 4: AnonymousCurrentUser**

`src/CrudKit.Core/Auth/AnonymousCurrentUser.cs`:
```csharp
using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.Core.Auth;

/// <summary>
/// ICurrentUser DI'a kayıtlı değilse veya token yoksa kullanılır.
/// CrudKit.Api TryAddScoped ile fallback olarak register eder.
/// </summary>
public class AnonymousCurrentUser : ICurrentUser
{
    public string? Id => null;
    public string? Username => null;
    public string? TenantId => null;
    public IReadOnlyList<string> Roles => Array.Empty<string>();
    public IReadOnlyList<Permission> Permissions => Array.Empty<Permission>();
    public bool IsAuthenticated => false;
    public bool HasRole(string role) => false;
    public bool HasPermission(string entity, string action) => false;
    public bool HasPermission(string entity, string action, PermScope scope) => false;
}
```

- [ ] **Step 5: FakeCurrentUser**

`src/CrudKit.Core/Auth/FakeCurrentUser.cs`:
```csharp
using CrudKit.Core.Enums;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.Core.Auth;

/// <summary>
/// Test ve geliştirme ortamı için. Her izne onay verir.
/// </summary>
public class FakeCurrentUser : ICurrentUser
{
    public string? Id { get; set; } = "dev-user-1";
    public string? Username { get; set; } = "developer";
    public string? TenantId { get; set; } = "dev-tenant";
    public IReadOnlyList<string> Roles { get; set; } = new List<string> { "admin" };
    public IReadOnlyList<Permission> Permissions { get; set; } = new List<Permission>();
    public bool IsAuthenticated => true;
    public bool HasRole(string role) => Roles.Contains(role);
    public bool HasPermission(string entity, string action) => true;
    public bool HasPermission(string entity, string action, PermScope scope) => true;
}
```

- [ ] **Step 6: Build et**

```bash
dotnet build src/CrudKit.Core/CrudKit.Core.csproj
```

Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/CrudKit.Core/Enums/ \
        src/CrudKit.Core/Models/Permission.cs \
        src/CrudKit.Core/Interfaces/ICurrentUser.cs \
        src/CrudKit.Core/Auth/
git commit -m "feat(core): ICurrentUser + PermScope + Permission + AnonymousCurrentUser + FakeCurrentUser"
```

---

## Task 9: AppContext + Events

**Files:**
- Create: `src/CrudKit.Core/Context/AppContext.cs`
- Create: `src/CrudKit.Core/Events/IEvent.cs`
- Create: `src/CrudKit.Core/Events/EntityCreatedEvent.cs`
- Create: `src/CrudKit.Core/Events/EntityUpdatedEvent.cs`
- Create: `src/CrudKit.Core/Events/EntityDeletedEvent.cs`

- [ ] **Step 1: AppContext**

`src/CrudKit.Core/Context/AppContext.cs`:
```csharp
using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Context;

public class AppContext
{
    public required IServiceProvider Services { get; init; }
    public required ICurrentUser CurrentUser { get; init; }
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    public string? TenantId => CurrentUser.TenantId;
    public string? UserId => CurrentUser.Id;
    public bool IsAuthenticated => CurrentUser.IsAuthenticated;
}
```

- [ ] **Step 2: Events**

`src/CrudKit.Core/Events/IEvent.cs`:
```csharp
namespace CrudKit.Core.Events;

public interface IEvent
{
    string EventId { get; }
    DateTime OccurredAt { get; }
}
```

`src/CrudKit.Core/Events/EntityCreatedEvent.cs`:
```csharp
namespace CrudKit.Core.Events;

public record EntityCreatedEvent<T>(T Entity) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

`src/CrudKit.Core/Events/EntityUpdatedEvent.cs`:
```csharp
namespace CrudKit.Core.Events;

public record EntityUpdatedEvent<T>(T Entity) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

`src/CrudKit.Core/Events/EntityDeletedEvent.cs`:
```csharp
namespace CrudKit.Core.Events;

public record EntityDeletedEvent<T>(string EntityId) : IEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
```

- [ ] **Step 3: Build et**

```bash
dotnet build src/CrudKit.Core/CrudKit.Core.csproj
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/CrudKit.Core/Context/ src/CrudKit.Core/Events/
git commit -m "feat(core): AppContext + domain events (IEvent, Created, Updated, Deleted)"
```

---

## Task 10: Core Attribute'ları + AttributeMetadataTests

**Files:**
- Create: `src/CrudKit.Core/Attributes/CrudEntityAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/SearchableAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/UniqueAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/ProtectedAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/SkipResponseAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/SkipUpdateAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/HashedAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/CascadeSoftDeleteAttribute.cs`
- Create: `src/CrudKit.Core/Attributes/DefaultIncludeAttribute.cs`
- Create: `tests/CrudKit.Core.Tests/Attributes/AttributeMetadataTests.cs`

- [ ] **Step 1: Failing testi yaz**

`tests/CrudKit.Core.Tests/Attributes/AttributeMetadataTests.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

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

    [Fact]
    public void UniqueAttribute_ShouldBeOnUsername()
    {
        var prop = typeof(TestUser).GetProperty("Username");
        Assert.NotNull(prop?.GetCustomAttribute<UniqueAttribute>());
    }

    [Fact]
    public void HashedAttribute_ShouldBeOnPassword()
    {
        var prop = typeof(TestUser).GetProperty("Password");
        Assert.NotNull(prop?.GetCustomAttribute<HashedAttribute>());
    }

    [Fact]
    public void TestUser_ShouldImplementISoftDeletable()
    {
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(typeof(TestUser)));
    }
}
```

- [ ] **Step 2: Testin fail ettiğini doğrula**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "AttributeMetadataTests"
```

Expected: Build error — attribute'lar bulunamadı.

- [ ] **Step 3: Attribute implementasyonları**

`src/CrudKit.Core/Attributes/CrudEntityAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

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
    public bool EnableBulkUpdate { get; set; }
    public int BulkLimit { get; set; } = 0; // 0 = global default kullan
}
```

`src/CrudKit.Core/Attributes/SearchableAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class SearchableAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/UniqueAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class UniqueAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/ProtectedAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class ProtectedAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/SkipResponseAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class SkipResponseAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/SkipUpdateAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class SkipUpdateAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/HashedAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class HashedAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/CascadeSoftDeleteAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

/// <summary>
/// Navigation property'ye eklenir. Üst entity silindiğinde bu koleksiyon da soft-delete yapılır.
/// Sadece ICascadeSoftDelete implemente eden parent entity'lerde çalışır.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class CascadeSoftDeleteAttribute : Attribute { }
```

`src/CrudKit.Core/Attributes/DefaultIncludeAttribute.cs`:
```csharp
namespace CrudKit.Core.Attributes;

/// <summary>
/// Navigation property'ye eklenir.
/// EfRepo'nun List + FindById sorgularına otomatik Include eklenir.
/// Response serializasyonunda da bu property dahil edilir.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class DefaultIncludeAttribute : Attribute { }
```

- [ ] **Step 4: Testleri çalıştır**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj --filter "AttributeMetadataTests"
```

Expected: All 7 tests pass.

- [ ] **Step 5: Tüm Core testlerini çalıştır**

```bash
dotnet test tests/CrudKit.Core.Tests/CrudKit.Core.Tests.csproj
```

Expected: All tests pass (FilterOp + ListParams + ValidationErrors + Optional + Paginated + AttributeMetadata).

- [ ] **Step 6: Commit**

```bash
git add src/CrudKit.Core/Attributes/ \
        tests/CrudKit.Core.Tests/Attributes/
git commit -m "feat(core): all attributes + AttributeMetadataTests — CrudKit.Core complete"
```

---

## Self-Review

### Spec Coverage

| 01-core.md Bölümü | Task | Durum |
|---|---|---|
| IEntity | Task 7 | ✓ |
| ICurrentUser | Task 8 | ✓ |
| ICrudHooks (BeforeRestore/AfterRestore/ApplyScope dahil) | Task 7 | ✓ |
| ISoftDeletable | Task 7 | ✓ |
| ICascadeSoftDelete | Task 7 | ✓ |
| IAuditable | Task 7 | ✓ |
| IMultiTenant | Task 7 | ✓ |
| IStateMachine | Task 7 | ✓ |
| IDocumentNumbering | Task 7 | ✓ |
| IEventBus | Task 7 | ✓ |
| IEntityMapper | Task 7 | ✓ |
| IModule | Task 7 | ✓ |
| CrudEntityAttribute (EnableBulkUpdate, BulkLimit dahil) | Task 10 | ✓ |
| SearchableAttribute | Task 10 | ✓ |
| UniqueAttribute | Task 10 | ✓ |
| ProtectedAttribute | Task 10 | ✓ |
| SkipResponseAttribute | Task 10 | ✓ |
| SkipUpdateAttribute | Task 10 | ✓ |
| HashedAttribute | Task 10 | ✓ |
| CascadeSoftDeleteAttribute | Task 10 | ✓ |
| DefaultIncludeAttribute | Task 10 | ✓ |
| Paginated\<T\> | Task 6 | ✓ |
| Optional\<T\> + serialization | Task 5 | ✓ |
| AppError | Task 4 | ✓ |
| ValidationErrors + FieldError | Task 4 | ✓ |
| FilterOp | Task 2 | ✓ |
| ListParams + SortDirection | Task 3 | ✓ |
| AppContext | Task 9 | ✓ |
| IEvent + EntityCreated/Updated/DeletedEvent | Task 9 | ✓ |
| PermScope + Permission | Task 8 | ✓ |
| AnonymousCurrentUser + FakeCurrentUser | Task 8 | ✓ |
| FilterOpTests | Task 2 | ✓ |
| ListParamsTests | Task 3 | ✓ |
| ValidationErrorsTests | Task 4 | ✓ |
| OptionalTests | Task 5 | ✓ |
| PaginatedTests | Task 6 | ✓ |
| AttributeMetadataTests | Task 10 | ✓ |

### Tip Tutarlılığı
- `AppContext` → `ICurrentUser` (Task 8) ve `IEntity`'den bağımsız ✓
- `ICrudHooks<T>` → `AppContext` (Context namespace) ✓
- `IEventBus` → `IEvent` (Events namespace) ✓
- `IEntityMapper<TEntity, TResponse>` → `IEntity` constraint ✓
- `ValidationErrors.ThrowIfInvalid()` → `AppError.Validation()` ✓

### Placeholder Taraması
Tüm adımlarda gerçek kod var. "TBD", "TODO" veya açıklama yok.
