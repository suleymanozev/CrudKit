using System.ComponentModel.DataAnnotations;
using CrudKit.Core.Interfaces;

namespace CrudKit.Sample.Api.Entities;

public class OrderLine : IAuditableEntity
{
    public Guid Id { get; set; }
    public string OrderId { get; set; } = string.Empty;

    [Required]
    public string ProductName { get; set; } = string.Empty;

    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
