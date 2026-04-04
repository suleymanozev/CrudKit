# CrudKit — Master Prompt Dosyaları

## Dosya Yapısı

```
crudkit-prompts/
├── 00-overview.md          Proje tanımı, solution yapısı, bağımlılıklar, tasarım ilkeleri, NuGet
├── 01-core.md              CrudKit.Core — interface, attribute, model tanımları
├── 02-entityframework.md   CrudKit.EntityFrameworkCore — CrudKitDbContext, EfRepo, Query, Dialect
├── 03-auth.md              ICurrentUser soyutlaması, Permission, endpoint filter'lar
├── 04-api.md               CrudKit.Api — endpoint mapping, handler'lar, schema
├── 05-workflow.md          CrudKit.Workflow — engine, ActionRegistry, step tipleri
├── 06-usage.md             Kullanıcı tarafı — entity tanımı, Program.cs, endpoint listesi
├── 07-tests.md             Tüm test dosyaları — Core, EF, Api, Workflow testleri
├── 08-edge-cases.md        14 edge case — Optional<T>, cascade, transaction, bulk, vb.
├── 09-sourcegen.md         CrudKit.SourceGen — DTO, mapper, endpoint, hook üretimi
└── README.md               Bu dosya
```

## Geliştirme Sırası

Her adımda ilgili prompt dosyasını kullan. Bir önceki adım tamamlanmadan sonrakine geçme.

```
Adım 1 → 00-overview.md + 01-core.md
          Solution oluştur, Core paketini yaz.
          Interface'ler, attribute'lar, modeller.
          Testler: 07-tests.md (7.1 Core Tests bölümü)

Adım 2 → 02-entityframework.md
          CrudKitDbContext, EfRepo, QueryBuilder, FilterApplier, Dialect.
          Soft delete, tenant, timestamp, audit — hepsi DbContext'te.
          Testler: 07-tests.md (7.2 EF Tests bölümü)

Adım 3 → 03-auth.md
          ICurrentUser interface, AnonymousCurrentUser, FakeCurrentUser.
          RequireAuth/Role/Permission filter'ları.
          Testler: 07-tests.md içindeki auth testleri

Adım 4 → 04-api.md
          CrudEndpointMapper, handler'lar, schema endpoint.
          ValidationFilter, AppErrorFilter, IdempotencyFilter.
          Testler: 07-tests.md (7.4 Api Tests bölümü)

Adım 5 → 08-edge-cases.md
          Optional<T>, cascade soft delete, transaction scope,
          include stratejisi, migration, idempotency, bulk operations,
          operation control, ResponseDto, concurrency, error handling,
          swagger, API versioning, logging.
          Her edge case'in testi kendi bölümünde.

Adım 6 → 05-workflow.md
          WorkflowEngine, ActionRegistry, step tipleri.
          DB-driven akış, config-driven action'lar.
          Testler: 07-tests.md (7.5 Workflow Tests bölümü)

Adım 7 → 09-sourcegen.md
          CreateDto, UpdateDto, ResponseDto, Mapper, EndpointMapping,
          HookStub generator'ları. Diagnostics.
          Testler: kendi bölümünde (12.12)

Adım 8 → 06-usage.md
          Doküman ve örnek proje ile doğrulama.
```

## Nasıl Kullanılır

Her dosya bağımsız bir prompt olarak kullanılabilir. Örnek:

```
"Bu prompt'a göre CrudKit.Core projesini oluştur:"
→ 01-core.md dosyasının içeriğini yapıştır

"Testleri yaz:"
→ 07-tests.md dosyasından ilgili bölümü yapıştır

"Edge case'leri implemente et:"
→ 08-edge-cases.md dosyasını yapıştır
```

## Dosya Boyutları

| Dosya | Satır | İçerik |
|-------|-------|--------|
| 00-overview.md | ~110 | Genel bakış, mimari kararlar |
| 01-core.md | ~300 | Interface + attribute + model |
| 02-entityframework.md | ~800 | DbContext + Repo + Query + Dialect |
| 03-auth.md | ~530 | ICurrentUser + permission + filter |
| 04-api.md | ~305 | Endpoint + handler + schema |
| 05-workflow.md | ~180 | Engine + registry + step |
| 06-usage.md | ~190 | Entity tanımı + Program.cs |
| 07-tests.md | ~1365 | Tüm testler |
| 08-edge-cases.md | ~4112 | 29 edge case (11.1–11.29) |
| 09-sourcegen.md | ~3130 | 6 generator + diagnostics + test |
| **Toplam** | **~9215** | |
