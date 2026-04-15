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

/// <summary>
/// Extends IStateMachine with typed payloads for specific transitions.
/// Only transitions that require a payload need to be listed in TransitionPayloads.
/// </summary>
public interface IStateMachineWithPayload<TState> : IStateMachine<TState>
    where TState : struct, Enum
{
    /// <summary>
    /// Maps action names to their required payload types.
    /// Actions not listed here accept no payload.
    /// </summary>
    static abstract IReadOnlyDictionary<string, Type> TransitionPayloads { get; }
}
