namespace CrudKit.Core.Interfaces;

/// <summary>
/// For entities with state machine behavior.
/// Transitions defines the valid state changes.
/// </summary>
public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}
