# Changelog

All notable changes to CrudKit are documented in this file.

## [1.1.0] - 2026-04-15

### Added
- `IStateMachineWithPayload<TState>` — typed payload support for state transitions
- `ITransitionHook<T>` — before/after hooks for state transitions with payload access
- `IGlobalCrudHook.BeforeRestore/AfterRestore` — cross-cutting restore hooks
- `IRepo<T>.FindByFilter` — untracked entity list by filter dictionary
- `IRepo<T>.FindDeletedById` — find soft-deleted entity bypassing soft-delete filter
- `IRepo<T>.BulkPurge` — bulk purge with audit trail
- `IRepo<T>.Create(dto, configureEntity, ct)` — create with post-mapping customization (used by child endpoints for FK injection)
- Purge audit entries — `IAuditWriter` now receives `Action = "Purge"` entries with serialized `OldValues` before physical deletion
- Property attributes docs guide (`docs/features/property-attributes.md`)
- Security guide (`docs/guides/security.md`)
- Enriched feature docs (idempotency, domain-events, value-objects, concurrency, bulk-operations, import-export, auth, audit-trail) with client examples and complete scenarios

### Changed
- **Hooks moved into repository** — `ICrudHooks<T>` / `IGlobalCrudHook` now invoked inside `EfRepo.Create/Update/Delete/Restore/BulkDelete/BulkUpdate`. Handlers delegate completely to repo. Child endpoints now also trigger hooks (previously silently skipped).
- `[Protected]` now blocks writes on Create as well as Update (previously only Update). Use `[SkipUpdate]` for the old "writable on Create" behavior.
- Import handler now invokes entity-specific hooks (`ICrudHooks<T>`) in addition to global hooks
- `RestoreHandler` now calls `BeforeRestore` **before** restore (previously called after, which made it misleading)
- Transition payload dictionary lookup is now case-insensitive (`StringComparer.OrdinalIgnoreCase`)
- Child create/batch endpoints no longer double-save when DTO lacks FK — uses new `Create(dto, configureEntity, ct)` overload
- CI skips for docs-only and markdown-only changes

### Removed
- **`/bulk-count` endpoint** — list endpoint already returns `total` in response. Use `GET /api/{resource}?{filters}&per_page=1` and read `total`.
- `IRepo<T>.BulkCount(filters)` renamed to `Count(filters)` (overload next to existing `Count()`)
- `CrudEntityAttribute.Workflow` / `WorkflowProtected` — never implemented, removed
- `IModule.RegisterWorkflowActions` — never called, removed
- `PermScope` parameter references in docs — type never existed in source

### Fixed
- Stale `bulk-count` references across README, intro, endpoints table, bulk-operations page
- `[Protected]` XML doc referenced non-existent `WorkflowProtectionFilter`
- Docs-site GitHub Pages subpath redirect (missing `baseUrl` prefix)
- NuGet Trusted Publishing workflow (added `NuGet/login@v1` step with user parameter)

### Breaking Changes
- Custom `IRepo<T>` implementations must add `FindByFilter`, `FindDeletedById`, `BulkPurge`, and the `Create(dto, configureEntity, ct)` overload
- Custom `IGlobalCrudHook` implementations inherit new `BeforeRestore/AfterRestore` (default no-op)
- Entity-as-DTO clients that relied on setting `[Protected]` fields on Create will no longer succeed — use `[SkipUpdate]` instead
- Clients using `POST /bulk-count` must migrate to `GET /api/{resource}?per_page=1` and read `total`

## [1.0.0] - 2026-04-12

### Core
- Entity-driven CRUD endpoint generation via `[CrudEntity]` attribute
- Entity base class hierarchy: `Entity`, `AuditableEntity`, `FullAuditableEntity` (with `WithUser<T>` variants)
- AggregateRoot hierarchy with domain event support (`IHasDomainEvents`)
- `IRepo<T>` built-in repository with `IEntity` constraint
- Entity-as-DTO — `MapCrudEndpoints<T>()` without custom DTOs
- `[CreateDtoFor]`, `[UpdateDtoFor]`, `[ResponseDtoFor]` for custom DTO discovery
- `Optional<T>` for partial update semantics
- Reflection metadata caching (`EntityMetadataCache`)
- System fields auto-hidden from API responses (TenantId, DeleteBatchId, DomainEvents)

### Endpoint Handlers
- 11 modular handlers: List, GetById, Create, Update, Delete, Restore, Purge, Bulk, Export, Import, Transition
- `IEndpointConfigurer<T>` — auto-discovered custom endpoint configuration
- `UseCrudKit()` assembly scan — auto-registers all `[CrudEntity]` types
- `[CrudEntity(ReadOnly = true)]` for read-only entities
- `[CrudEntity(EnableCreate/Update/Delete/BulkDelete/BulkUpdate)]` fine-grained control

### Soft Delete
- `ISoftDeletable` with `DeletedAt` timestamp
- `[CascadeSoftDelete]` for child entity cascade
- `DeleteBatchId` — smart cascade restore (only restores batch-deleted children)
- Purge endpoints for permanent deletion

### Multi-Tenancy
- `IMultiTenant` with automatic query filter
- 5 tenant resolvers (header, claim, subdomain, route, query)
- `CrossTenantPolicy` with role-based access
- Tenant filter cannot be disabled (security by design)
- `[Unique]` and `[CrudIndex]` auto-prepend TenantId

### Audit Trail
- `[Audited]` / `[NotAudited]` per-entity
- `[AuditIgnore]` per-property
- `CorrelationId` linking entries from same SaveChanges
- Failed operation logging (`EnableAuditFailedOperations`)
- Configurable schema and context

### State Machine
- `IStateMachine<TState>` with declarative transitions
- `POST /{id}/transition/{action}` auto-generated
- `[Protected]` on Status field prevents direct update

### Auto-Sequence
- `[AutoSequence("INV-{year}-{seq:5}")]` with template tokens
- Per-tenant, per-entity, per-prefix isolation
- Atomic SQL upsert (no race conditions)
- `ISequenceCustomizer<T>` for custom placeholders and DB-driven templates
- Default template `{seq:5}` when no template specified

### Domain Events
- `IDomainEvent`, `IHasDomainEvents`, `IDomainEventHandler<T>`
- Automatic dispatch after SaveChanges
- `CrudKitEventDispatcher` default dispatcher
- `UseDomainEvents<TCustomDispatcher>()` for custom dispatchers

### Hooks
- `ICrudHooks<T>` per-entity lifecycle hooks
- `IGlobalCrudHook` cross-entity hooks
- `existingEntity` parameter on BeforeUpdate/AfterUpdate
- `ApplyScope` for row-level security
- `ApplyIncludes` for custom eager loading

### Child Entities
- `[ChildOf(typeof(Parent))]` declarative auto-discovery
- `WithChild<T, TCreate, TUpdate>()` explicit with update support
- Auto-discovered `[CreateDtoFor]` / `[UpdateDtoFor]` for child DTOs
- Batch upsert endpoint

### Value Objects
- `[ValueObject]` marks a class as value object
- `[Flatten]` flattens VO properties in DTOs (PriceAmount, PriceCurrency)
- Nested JSON without `[Flatten]` (default)
- Partial update support for flattened VOs

### Indexing
- `[Unique]` — tenant-scoped unique index with soft-delete partial filter
- `[CrudIndex]` — composite indexes, `TenantAware` flag, custom `Name`
- Automatic TenantId prepend for `IMultiTenant` entities

### Validation
- FluentValidation priority, DataAnnotation fallback
- `[Required]`, `[MaxLength]`, `[Range]` on entity and DTO properties

### Authorization
- `[RequireAuth]`, `[RequireRole]`, `[RequirePermissions]`
- `[AuthorizeOperation("Delete", "admin")]` per-operation
- `Authorize()` fluent builder on endpoint groups

### Configuration
- `CrudKitDbContextDependencies` — 2-parameter DbContext constructor
- `UseModuleSchema()` — cross-provider schema support
- `MinPageSize` / `MaxPageSize` with `Math.Clamp`
- `BulkLimit` default 1,000

### Database Support
- PostgreSQL (Npgsql)
- SQL Server
- SQLite
- MySQL / MariaDB
- `DialectDetector` auto-detection
- Schema validation (SQLite + schema = startup error)

### Bulk Operations
- Hook-aware — entities loaded, ProcessBeforeSave runs
- System fields blocked from bulk update
- `[Protected]` and `[SkipUpdate]` respected in bulk update

### Security
- 30 security tests covering OWASP API Top 10
- Mass assignment protection (system fields, Protected, SkipUpdate)
- Tenant isolation (4 tests, 3-layer protection)
- Filter value length limit (500 chars)
- Unique constraint returns 409 Conflict (not 500)
- Error messages don't leak internal details

### Testing
- 605 tests total
- Testcontainers for PostgreSQL integration tests
- Provider-agnostic test infrastructure (SQLite default, PostgreSQL via Docker)
- 16 edge-case tests
- Performance tests for bulk operations
