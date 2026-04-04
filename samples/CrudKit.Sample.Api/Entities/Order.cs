using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;

namespace CrudKit.Sample.Api.Entities;

public enum OrderStatus { Pending, Processing, Completed, Cancelled }

[CrudEntity(Table = "orders", SoftDelete = true)]
public class Order : IEntity, ISoftDeletable, IStateMachine<OrderStatus>
{
    public string Id { get; set; } = string.Empty;

    [Required]
    public string CustomerName { get; set; } = string.Empty;

    public decimal Total { get; set; }

    [Protected]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}
