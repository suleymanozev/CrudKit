namespace CrudKit.Core.Interfaces;

/// <summary>
/// Runtime toggle for global query filters (soft-delete, multi-tenant).
/// Use Disable() to temporarily bypass a filter within a using scope.
/// </summary>
public interface IDataFilter<TFilter> where TFilter : class
{
    /// <summary>Returns true when the filter is currently active.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Disables the filter for the duration of the returned scope.
    /// The filter is automatically re-enabled when the scope is disposed.
    /// </summary>
    IDisposable Disable();

    /// <summary>
    /// Enables the filter for the duration of the returned scope.
    /// The filter is automatically restored to its previous state when the scope is disposed.
    /// </summary>
    IDisposable Enable();
}
