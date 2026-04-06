using CrudKit.EntityFrameworkCore.Models;
using CrudKit.EntityFrameworkCore.Tests.Helpers;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Models;

public class InfraModelsTests
{
    [Fact]
    public void AuditLogEntry_HasRequiredProperties()
    {
        var entry = new AuditLogEntry
        {
            EntityType = "Order",
            EntityId = "123",
            Action = "Create",
            UserId = "u1",
            Timestamp = DateTime.UtcNow,
            NewValues = "{}",
        };

        Assert.Equal("Order", entry.EntityType);
        Assert.Equal("123", entry.EntityId);
        Assert.Equal("Create", entry.Action);
        Assert.NotEqual(Guid.Empty, entry.Id);
    }

    [Fact]
    public void SequenceEntry_HasRequiredProperties()
    {
        var seq = new SequenceEntry
        {
            EntityType = "Invoice",
            TenantId = "t1",
            Year = "2026",
            CurrentVal = 42,
        };

        Assert.Equal("Invoice", seq.EntityType);
        Assert.Equal(42, seq.CurrentVal);
        Assert.NotEqual(Guid.Empty, seq.Id);
    }

    [Fact]
    public void IConcurrent_RowVersion_IsUint()
    {
        var entity = new ConcurrentEntity { RowVersion = 7 };
        Assert.Equal(7u, entity.RowVersion);
    }
}
