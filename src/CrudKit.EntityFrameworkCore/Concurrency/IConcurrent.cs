namespace CrudKit.EntityFrameworkCore.Concurrency;

/// <summary>
/// Opt-in optimistic concurrency. CrudKitDbContext configures RowVersion as an EF rowversion column.
/// </summary>
public interface IConcurrent
{
    uint RowVersion { get; set; }
}
