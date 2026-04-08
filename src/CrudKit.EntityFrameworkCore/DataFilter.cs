using CrudKit.Core.Interfaces;

namespace CrudKit.EntityFrameworkCore;

/// <summary>
/// Scoped implementation of <see cref="IDataFilter{TFilter}"/>.
/// Tracks the enabled/disabled state and restores the previous state when the scope is disposed.
/// Each HTTP request (DI scope) gets its own instance so filter state is request-isolated.
/// </summary>
public class DataFilter<TFilter> : IDataFilter<TFilter> where TFilter : class
{
    /// <inheritdoc />
    public bool IsEnabled { get; private set; } = true;

    /// <inheritdoc />
    public IDisposable Disable()
    {
        var previous = IsEnabled;
        IsEnabled = false;
        return new FilterScope(() => IsEnabled = previous);
    }

    /// <inheritdoc />
    public IDisposable Enable()
    {
        var previous = IsEnabled;
        IsEnabled = true;
        return new FilterScope(() => IsEnabled = previous);
    }

    private sealed class FilterScope : IDisposable
    {
        private readonly Action _restore;
        public FilterScope(Action restore) => _restore = restore;
        public void Dispose() => _restore();
    }
}
