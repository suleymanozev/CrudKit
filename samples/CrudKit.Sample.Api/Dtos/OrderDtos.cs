using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Models;
using CrudKit.Sample.Api.Entities;

namespace CrudKit.Sample.Api.Dtos;

[CreateDtoFor(typeof(Order))]
public record CreateOrder(
    [Required] string CustomerName,
    decimal Total = 0);

[UpdateDtoFor(typeof(Order))]
public record UpdateOrder
{
    public Optional<string?> CustomerName { get; init; }
    public Optional<decimal?> Total { get; init; }
}

[CreateDtoFor(typeof(OrderLine))]
public record CreateOrderLine(
    Guid OrderId,
    [Required] string ProductName,
    int Quantity = 1,
    decimal UnitPrice = 0);
