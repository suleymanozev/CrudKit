namespace CrudKit.Core.Interfaces;

/// <summary>
/// Automatic document number generation (e.g. INV-2025-001).
/// DocumentNumber is assigned automatically when EfRepo.Create is called.
/// </summary>
public interface IDocumentNumbering
{
    string DocumentNumber { get; set; }
    static abstract string Prefix { get; }
    static abstract bool YearlyReset { get; }
}
