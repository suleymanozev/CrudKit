using CrudKit.SourceGen.Diagnostics;
using CrudKit.SourceGen.Models;
using Xunit;

namespace CrudKit.SourceGen.Tests.Diagnostics;

public class EntityValidatorTests
{
    private static EntityMetadata MakeEntity(
        bool implementsIEntity = true,
        bool softDelete = false, bool implementsISoftDeletable = false,
        bool multiTenant = false, bool implementsIMultiTenant = false,
        string table = "test_table") =>
        new EntityMetadata("Test", "TestNs", "TestNs.Test", table,
            softDelete, false, multiTenant, false, true, true, true, false, false, null,
            implementsIEntity, implementsISoftDeletable, implementsIMultiTenant, []);

    [Fact]
    public void CRUD001_MissingIEntity()
    {
        var entity = MakeEntity(implementsIEntity: false);
        var diags = EntityValidator.Validate(entity, null);
        Assert.Contains(diags, d => d.Id == "CRUD001");
    }

    [Fact]
    public void CRUD002_SoftDeleteWithoutInterface()
    {
        var entity = MakeEntity(softDelete: true, implementsISoftDeletable: false);
        var diags = EntityValidator.Validate(entity, null);
        Assert.Contains(diags, d => d.Id == "CRUD002");
    }

    [Fact]
    public void CRUD003_MultiTenantWithoutInterface()
    {
        var entity = MakeEntity(multiTenant: true, implementsIMultiTenant: false);
        var diags = EntityValidator.Validate(entity, null);
        Assert.Contains(diags, d => d.Id == "CRUD003");
    }

    [Fact]
    public void CRUD010_EmptyTableName()
    {
        var entity = MakeEntity(table: "");
        var diags = EntityValidator.Validate(entity, null);
        Assert.Contains(diags, d => d.Id == "CRUD010");
    }

    [Fact]
    public void ValidEntity_NoDiagnostics()
    {
        var entity = MakeEntity(softDelete: true, implementsISoftDeletable: true,
            multiTenant: true, implementsIMultiTenant: true);
        var diags = EntityValidator.Validate(entity, null);
        Assert.Empty(diags);
    }
}
