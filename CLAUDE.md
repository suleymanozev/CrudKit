# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Language

Always respond in Turkish. All source code comments and XML docs must be in English.

## Build & Test Commands

```bash
# Build entire solution
dotnet build CrudKit.slnx

# Run all tests (565 tests across 5 projects)
dotnet test CrudKit.slnx

# Run tests for a specific project
dotnet test tests/CrudKit.Api.Tests/
dotnet test tests/CrudKit.EntityFrameworkCore.Tests/
dotnet test tests/CrudKit.Core.Tests/
dotnet test tests/CrudKit.Identity.Tests/

# Run a single test
dotnet test tests/CrudKit.Api.Tests/ --filter "ClassName.MethodName"

# Run tests matching a pattern
dotnet test tests/CrudKit.Api.Tests/ --filter "FullyQualifiedName~PurgeEndpoint"

# Documentation site (Docusaurus)
cd docs-site && npm start        # dev server
cd docs-site && npx docusaurus build  # verify build
```

## Architecture

CrudKit is a convention-based CRUD framework for .NET 10. Entity definition drives everything — endpoints, validation, auth, audit, soft delete, tenancy.

### Package Dependency Graph

```
CrudKit.Core (no dependencies)
    ↓              ↓
CrudKit.Api      CrudKit.EntityFrameworkCore
(→ Core only)    (→ Core only)
                     ↓
                 CrudKit.Identity (→ EF + Core)
```

### Key Abstraction: ICrudKitDbContext

Both `CrudKitDbContext` and `CrudKitIdentityDbContext` implement `ICrudKitDbContext`. All infrastructure (`EfRepo`, `DbAuditWriter`, `CrudEndpointMapper`) depends on this interface, not concrete classes. Shared logic lives in `CrudKitDbContextHelper` (static methods).

### Request Flow

```
HTTP Request
  → TenantResolverMiddleware (sets ITenantContext.TenantId)
  → CrudEndpointMapper handler
    → AppErrorFilter (catches exceptions → structured JSON)
    → CrudAuthorizationFilter (per-operation role/permission check)
    → ValidationFilter (FluentValidation → DataAnnotation fallback)
    → IRepo<T>.Create/Update/Delete/List
      → EfRepo<T> (tries ICreateMapper/IUpdateMapper first, reflection fallback)
        → CrudKitDbContext.SaveChanges
          → CrudKitDbContextHelper.ProcessBeforeSave (timestamps, tenant, user tracking, soft-delete, RowVersion++)
          → base.SaveChanges
          → CrudKitDbContextHelper.ExecuteCascadeOps (cascade soft-delete SQL)
          → IAuditWriter.WriteAsync (if audit enabled)
```

### Entity Base Class Hierarchy

```
Entity<TKey> → Entity (Guid default)
AuditableEntity<TKey> → AuditableEntity (+ CreatedAt, UpdatedAt)
AuditableEntityWithUser<TKey,TUser,TUserKey> → AuditableEntityWithUser<TUser> (+ CreatedBy, UpdatedBy)
FullAuditableEntity<TKey> → FullAuditableEntity (+ DeletedAt, implements ISoftDeletable)
FullAuditableEntityWithUser<TKey,TUser,TUserKey> → FullAuditableEntityWithUser<TUser> (+ DeletedBy)
```

### 3-Level Feature Flags

```
Property attribute > Entity attribute > Global flag

Example (Export):
  Global: opts.UseExport()           → all entities exportable
  Entity: [NotExportable]            → this entity opts out
  Property: [NotExportable]          → this field excluded from export

Same pattern: Audited/NotAudited, Exportable/NotExportable, Importable/NotImportable,
              Filterable/NotFilterable, Sortable/NotSortable
```

### Auto Registration

`UseCrudKit()` scans all loaded assemblies for `[CrudEntity]`-decorated types and registers CRUD endpoints automatically. Discovers `[CreateDtoFor]`/`[UpdateDtoFor]` DTOs in the entity's assembly. Skips entities already registered by modules or manual `MapCrudEndpoints` calls.

### Multi-DbContext (Modular Monolith)

`CrudKitContextRegistry` maps entity types to their DbContext via `DbSet<T>` property scanning. `AddCrudKitEf<TContext>()` can be called multiple times. `EfRepo<T>` resolves the correct context automatically.

### Test Patterns

- **EF tests**: `DbHelper.CreateDb()` → in-memory SQLite, returns `TestDbContext`
- **API tests**: `TestWebApp.CreateAsync(configureEndpoints, configureServices)` → in-process web app with test server

### Master-Child Relationships

Two approaches (both work together):
- **Declarative**: `[ChildOf(typeof(Parent))]` on child entity → auto-discovered in `MapCrudEndpoints`
- **Explicit**: `.WithChild<TChild, TCreateChild>(route, fk)` for full control with typed Create DTO

### Tenant Resolution

`ITenantContext` (separate from `ICurrentUser`) resolved per-request via middleware:
```csharp
opts.UseMultiTenancy()
    .ResolveTenantFromHeader("X-Tenant-Id")
    .RejectUnresolvedTenant()
    .CrossTenantPolicy(policy => { ... });
```

3-layer protection: middleware (reject unresolved), middleware (AccessibleTenants check), EfRepo (null tenant guard).
