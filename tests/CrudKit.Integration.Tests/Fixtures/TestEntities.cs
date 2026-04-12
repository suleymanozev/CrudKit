using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Attributes;
using CrudKit.Core.Entities;
using CrudKit.Core.Interfaces;

namespace CrudKit.Integration.Tests.Fixtures;

[CrudEntity]
[CascadeSoftDelete(typeof(OrderLineEntity), nameof(OrderLineEntity.OrderEntityId))]
public class OrderEntity : FullAuditableAggregateRoot, IMultiTenant, IStateMachine<OrderStatus>
{
    [Required, MaxLength(200)] public string CustomerName { get; set; } = "";
    [AutoSequence("ORD-{year}-{seq:5}")]
    public string OrderNumber { get; set; } = "";
    [Protected]
    public OrderStatus Status { get; set; } = OrderStatus.Draft;
    public string TenantId { get; set; } = "";

    public static IReadOnlyList<(OrderStatus From, OrderStatus To, string Action)> Transitions =>
    [
        (OrderStatus.Draft, OrderStatus.Confirmed, "confirm"),
        (OrderStatus.Confirmed, OrderStatus.Shipped, "ship"),
        (OrderStatus.Draft, OrderStatus.Cancelled, "cancel"),
    ];
}

public enum OrderStatus { Draft = 1, Confirmed = 2, Shipped = 3, Cancelled = 4 }

[CrudEntity]
[ChildOf(typeof(OrderEntity), ForeignKey = "OrderEntityId")]
public class OrderLineEntity : FullAuditableAggregateRoot, IMultiTenant
{
    public Guid OrderEntityId { get; set; }
    [Required, MaxLength(200)] public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string TenantId { get; set; } = "";
}

[CrudEntity]
public class TenantAwareItem : FullAuditableAggregateRoot, IMultiTenant
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    public string TenantId { get; set; } = "";
}

[CrudEntity]
public class TenantUniqueItem : FullAuditableAggregateRoot, IMultiTenant
{
    [Required, MaxLength(50), Unique]
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "";
}

[CrudEntity]
[CrudIndex("Code", "Category", IsUnique = true)]           // → (TenantId, Code, Category) unique
[CrudIndex("Category")]                                     // → (TenantId, Category) non-unique
[CrudIndex("Priority", TenantAware = false)]               // → (Priority) tenant-independent
[CrudIndex("Code", IsUnique = true, Name = "IX_Custom_Code")] // → custom name, (TenantId, Code)
public class IndexedItem : FullAuditableAggregateRoot, IMultiTenant
{
    [Required, MaxLength(50)]
    public string Code { get; set; } = "";
    [MaxLength(100)]
    public string Category { get; set; } = "";
    public int Priority { get; set; }
    public string TenantId { get; set; } = "";
}
