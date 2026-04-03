namespace CrudKit.Core.Models;

/// <summary>
/// Paginated query result. Returned by EfRepo.List().
/// </summary>
public class Paginated<T>
{
    public List<T> Data { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalPages { get; set; }
}
