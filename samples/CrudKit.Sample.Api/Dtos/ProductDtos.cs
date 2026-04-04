using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Models;

namespace CrudKit.Sample.Api.Dtos;

public record CreateProduct(
    [Required, MaxLength(200)] string Name,
    [Range(0.01, 999_999.99)] decimal Price,
    string? Description = null,
    string Sku = "");

public record UpdateProduct
{
    public Optional<string?> Name { get; init; }
    public Optional<decimal?> Price { get; init; }
    public Optional<string?> Description { get; init; }
}
