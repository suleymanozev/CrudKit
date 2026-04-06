using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Models;
using CrudKit.Sample.Api.Entities;

namespace CrudKit.Sample.Api.Dtos;

[CreateDtoFor(typeof(Product))]
public record CreateProduct(
    [Required, MaxLength(200)] string Name,
    [Range(0.01, 999_999.99)] decimal Price,
    string? Description = null,
    string Sku = "");

[UpdateDtoFor(typeof(Product))]
public record UpdateProduct
{
    public Optional<string?> Name { get; init; }
    public Optional<decimal?> Price { get; init; }
    public Optional<string?> Description { get; init; }
}
