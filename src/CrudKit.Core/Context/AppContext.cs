using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Context;

/// <summary>
/// Per-request context passed to hooks and handlers.
/// Provides access to the current user, tenant context, and the DI container.
/// </summary>
public class AppContext
{
    public required IServiceProvider Services { get; init; }
    public required ICurrentUser CurrentUser { get; init; }
    public ITenantContext? TenantContext { get; init; }
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    public string? TenantId => TenantContext?.TenantId;
    public string? UserId => CurrentUser.Id;
    public bool IsAuthenticated => CurrentUser.IsAuthenticated;
}
