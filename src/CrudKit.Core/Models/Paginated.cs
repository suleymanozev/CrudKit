namespace CrudKit.Core.Models;

/// <summary>
/// Sayfalandırılmış sorgu sonucu. EfRepo.List() bu tipi döner.
/// </summary>
public class Paginated<T>
{
    public List<T> Data { get; set; } = new();
    public long Total { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public int TotalPages { get; set; }
}
