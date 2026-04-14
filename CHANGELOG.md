# Changelog

All notable changes to CrudKit are documented in this file.

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
