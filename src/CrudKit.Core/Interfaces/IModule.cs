using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Modular monolith support. Each module implements this interface.
/// AddCrudKit() discovers modules via assembly scan or manual registration.
/// </summary>
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
}
