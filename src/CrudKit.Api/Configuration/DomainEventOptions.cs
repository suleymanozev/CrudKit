using System.Reflection;

namespace CrudKit.Api.Configuration;

/// <summary>
/// Configuration for domain event handler scanning.
/// </summary>
public class DomainEventOptions
{
    internal List<Assembly> Assemblies { get; } = [];

    /// <summary>
    /// Scan the specified assembly for IDomainEventHandler implementations and register them in DI.
    /// </summary>
    public DomainEventOptions ScanHandlersFromAssembly(Assembly assembly)
    {
        Assemblies.Add(assembly);
        return this;
    }
}
