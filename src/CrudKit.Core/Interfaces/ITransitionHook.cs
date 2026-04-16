namespace CrudKit.Core.Interfaces;

/// <summary>
/// Lifecycle hooks for state machine transitions.
/// Resolved from DI per entity type. All methods have empty default implementations.
/// </summary>
public interface ITransitionHook<T> where T : class, IEntity
{
    /// <summary>
    /// Called before the status is changed. Payload is the deserialized typed payload (or null if none required).
    /// Throw to abort the transition.
    /// </summary>
    Task BeforeTransition(T entity, string action, object? payload, Context.AppContext ctx) => Task.CompletedTask;

    /// <summary>
    /// Called after the status is changed and saved.
    /// </summary>
    Task AfterTransition(T entity, string action, object? payload, Context.AppContext ctx) => Task.CompletedTask;
}
