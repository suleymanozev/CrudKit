using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Core.Interfaces;

/// <summary>
/// Modular monolith desteği. Her modül bu interface'i implemente eder.
/// AddCrudKit() assembly scan veya manuel kayıt ile modülleri bulur.
/// </summary>
public interface IModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration config);
    void MapEndpoints(WebApplication app);
    void RegisterWorkflowActions(object registry) { }
}
