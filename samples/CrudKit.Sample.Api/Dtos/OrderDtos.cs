using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Models;

namespace CrudKit.Sample.Api.Dtos;

public record CreateOrder(
    [Required] string CustomerName,
    decimal Total = 0);

public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
    public Optional<decimal?> Total { get; init; }
}

public record CreateOrderLine(
    Guid OrderId,
    [Required] string ProductName,
    int Quantity = 1,
    decimal UnitPrice = 0);
