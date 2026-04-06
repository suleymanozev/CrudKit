using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Entities;
using CrudKit.Core.Interfaces;

namespace CrudKit.Sample.Api.Entities;

public enum OrderStatus { Pending, Processing, Completed, Cancelled }

[CrudEntity(Table = "orders")]
[Audited]
[RequireAuth]
[AuthorizeOperation("Delete", "admin")]
[CascadeSoftDelete(typeof(OrderLine), nameof(OrderLine.OrderId))]
public class Order : FullAuditableEntity, IStateMachine<OrderStatus>
{
    [Required]
    public string CustomerName { get; set; } = string.Empty;

    public decimal Total { get; set; }

    [Protected]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Pending,    OrderStatus.Processing, "process"),
        (OrderStatus.Processing, OrderStatus.Completed,  "complete"),
        (OrderStatus.Pending,    OrderStatus.Cancelled,  "cancel"),
        (OrderStatus.Processing, OrderStatus.Cancelled,  "cancel"),
    ];
}
