using CrudKit.Core.Interfaces;

namespace CrudKit.Api.Endpoints;

/// <summary>
/// Implement to add custom endpoints to an entity's CRUD route group.
/// Discovered automatically by MapAllCrudEndpoints via entity assembly scanning.
/// No DI registration needed — just implement the interface.
/// </summary>
public interface IEndpointConfigurer<TEntity> where TEntity : class, IEntity
{
    /// <summary>
    /// Called after CRUD endpoints are registered for <typeparamref name="TEntity"/>.
    /// Use <see cref="CrudEndpointGroup{TMaster}.WithCustomEndpoints"/> to add routes.
    /// </summary>
    void Configure(CrudEndpointGroup<TEntity> group);
}
