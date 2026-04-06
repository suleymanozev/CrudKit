using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Models;
using CrudKit.Sample.Api.Entities;

namespace CrudKit.Sample.Api.Dtos;

[CreateDtoFor(typeof(Category))]
public record CreateCategory(
    [Required, MaxLength(100)] string Name,
    int SortOrder = 0);

[UpdateDtoFor(typeof(Category))]
public record UpdateCategory
{
    public Optional<string?> Name { get; init; }
    public Optional<int?> SortOrder { get; init; }
}
