using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Context;

/// <summary>
/// Request context — hook'lara ve handler'lara geçirilir.
/// Kullanıcı bilgisi ve DI container'a erişim sağlar.
/// </summary>
public class AppContext
{
    public required IServiceProvider Services { get; init; }
    public required ICurrentUser CurrentUser { get; init; }
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    public string? TenantId => CurrentUser.TenantId;
    public string? UserId => CurrentUser.Id;
    public bool IsAuthenticated => CurrentUser.IsAuthenticated;
}
