using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Bundles all CrudKit infrastructure dependencies for DbContext constructors.
/// Register once via AddCrudKitEf; resolved automatically by DI.
/// New dependencies can be added without breaking existing DbContext subclasses.
/// </summary>
public sealed class CrudKitDbContextDependencies
{
    public required ICurrentUser CurrentUser { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public CrudKitEfOptions? EfOptions { get; init; }
    public ITenantContext? TenantContext { get; init; }
    public IAuditWriter? AuditWriter { get; init; }
    public IDataFilter<ISoftDeletable>? SoftDeleteFilter { get; init; }
    public IDataFilter<IMultiTenant>? TenantFilter { get; init; }
    public IDomainEventDispatcher? DomainEventDispatcher { get; init; }
}
