namespace CrudKit.Core.Interfaces;

/// <summary>Fiziksel silme yerine DeletedAt alanı set eden entity'ler için.</summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
