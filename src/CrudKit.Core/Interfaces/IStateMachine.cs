namespace CrudKit.Core.Interfaces;

/// <summary>
/// State machine (durum makinesi) davranışı olan entity'ler için.
/// Transitions, geçerli durum geçişlerini tanımlar.
/// </summary>
public interface IStateMachine<TState> where TState : struct, Enum
{
    TState Status { get; set; }
    static abstract IReadOnlyList<(TState From, TState To, string Action)> Transitions { get; }
}
