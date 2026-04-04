using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Models;

namespace CrudKit.Sample.Api.Dtos;

public record CreateCategory(
    [Required, MaxLength(100)] string Name,
    int SortOrder = 0);

public record UpdateCategory
{
    public Optional<string?> Name { get; init; }
    public Optional<int?> SortOrder { get; init; }
}
