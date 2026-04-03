namespace CrudKit.Core.Interfaces;

/// <summary>
/// Otomatik belge numarası üretimi (ör: INV-2025-001).
/// EfRepo.Create çağrıldığında DocumentNumber otomatik atanır.
/// </summary>
public interface IDocumentNumbering
{
    string DocumentNumber { get; set; }
    static abstract string Prefix { get; }
    static abstract bool YearlyReset { get; }
}
