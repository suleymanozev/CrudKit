# CRUDKIT — C# MASTER PROMPT

## Proje Tanımı

ASP.NET Core Minimal API ile büyük ölçekli uygulamalar için generic, genişletilebilir bir CRUD altyapı framework'ü oluştur. Framework, attribute bazlı entity tanımından otomatik olarak CRUD endpoint'leri, validasyon, filtreleme, sayfalama, auth, soft delete, audit log, workflow entegrasyonu ve daha fazlasını sağlar.

Hedef: 85+ entity'li projelerde boilerplate'i minimuma indirmek. Developer sadece entity sınıfı ve iş mantığı hook'larını yazar, geri kalan her şey generic altyapı tarafından sağlanır.

---

## Solution Yapısı

```
CrudKit.sln
│
├── src/
│   ├── CrudKit.Core/                        # Temel interface, attribute, model — bağımlılık yok
│   │                                        # ICurrentUser, Permission, PermScope burada tanımlı
│   ├── CrudKit.EntityFrameworkCore/          # EF Core repo, query builder, tenant filter, migration
│   ├── CrudKit.Api/                          # Endpoint mapping, validation filter, schema endpoint, auth filter'lar
│   ├── CrudKit.Workflow/                     # Workflow engine, action registry, approval (opsiyonel)
│   └── CrudKit.SourceGen/                   # Source Generator — DTO, endpoint mapping otomatik üretimi (opsiyonel)
│
└── tests/
    ├── CrudKit.Core.Tests/
    ├── CrudKit.EntityFrameworkCore.Tests/
    ├── CrudKit.Api.Tests/
    ├── CrudKit.Workflow.Tests/
    └── CrudKit.SourceGen.Tests/
```

---

## Paket Bağımlılık Zinciri

```
CrudKit.Core                → Bağımlılık yok (sadece BCL + System.Text.Json)
CrudKit.EntityFrameworkCore → CrudKit.Core, Microsoft.EntityFrameworkCore
CrudKit.Api                 → CrudKit.Core, CrudKit.EntityFrameworkCore, Microsoft.AspNetCore.App
CrudKit.Workflow            → CrudKit.Core, CrudKit.EntityFrameworkCore
CrudKit.SourceGen           → CrudKit.Core (analyzer olarak referans — runtime bağımlılığı yok)
```

Kullanıcı sadece ihtiyacı olanı ekler:
- Basit CRUD → `CrudKit.Api` (geçişli olarak Core, EF gelir)
- Workflow lazım → `CrudKit.Workflow` ekle
- DTO/endpoint otomatik üretimi → `CrudKit.SourceGen` ekle
- Auth → Kullanıcı kendi auth'unu kurar, `ICurrentUser` implemente eder

---


## 8. Tasarım İlkeleri

1. **Generic her şeyi çözsün:** `MapCrudEndpoints<T, TCreate, TUpdate>` tek çağrı ile 5+ endpoint
2. **Attribute ile konfigürasyon:** `[CrudEntity]`, `[Searchable]`, `[Protected]` vb. ile entity davranışı belirlenir
3. **Hook ile genişletme:** `ICrudHooks<T>` interface'i ile lifecycle'a müdahale. Default implementasyon boş, sadece gerekeni override et
4. **Her zaman escape hatch:** Generic yapıya sığmayan entity'ler `IEntity` implemente edip kendi endpoint'lerini yazabilir, `IRepo<T>` ve `QueryBuilder<T>` kullanmaya devam eder
5. **Config-driven workflow:** Akış DB'de, action'lar kodda. Aynı action farklı config ile farklı davranış
6. **Reflection tabanlı metadata:** Entity property'lerinden otomatik ColumnMeta üretimi. Schema endpoint ile frontend'e metadata sağlama
7. **Interceptor pattern:** Timestamp, audit, soft delete gibi cross-cutting concern'ler EF Core interceptor'ları ile
8. **Paket bağımsızlığı:** Core bağımlılıksız. EF, Auth, Api, Workflow ayrı paketler. Kullanıcı sadece ihtiyacını ekler

---

## 9. NuGet Paket Bilgileri

```xml
<!-- Her proje için ortak -->
<PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <Authors>YourName</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryUrl>https://github.com/yourname/crudkit</RepositoryUrl>
</PropertyGroup>
```

### Bağımlılıklar

```xml
<!-- CrudKit.Core — bağımlılık yok -->

<!-- CrudKit.EntityFrameworkCore -->
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Relational" Version="9.*" />
<PackageReference Include="BCrypt.Net-Next" Version="4.*" />

<!-- CrudKit.Api -->
<!-- CrudKit.Core, CrudKit.EntityFrameworkCore referansları -->

<!-- CrudKit.Workflow -->
<!-- CrudKit.Core, CrudKit.EntityFrameworkCore referansları -->

<!-- Test projeleri -->
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
<PackageReference Include="xunit" Version="2.*" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.*" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="9.*" />
```

---

