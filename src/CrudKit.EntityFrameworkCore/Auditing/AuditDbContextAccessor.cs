using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.EntityFrameworkCore.Auditing;

/// <summary>
/// Determines which DbContext the audit writer should use.
/// When AuditContextType is set, resolves that specific context.
/// Otherwise falls back to the default ICrudKitDbContext.
/// </summary>
public class AuditDbContextAccessor
{
    private readonly Type? _contextType;

    public AuditDbContextAccessor(Type? contextType = null)
    {
        _contextType = contextType;
    }

    public ICrudKitDbContext Resolve(IServiceProvider services)
    {
        if (_contextType is not null)
            return (ICrudKitDbContext)services.GetRequiredService(_contextType);

        return services.GetRequiredService<ICrudKitDbContext>();
    }
}
