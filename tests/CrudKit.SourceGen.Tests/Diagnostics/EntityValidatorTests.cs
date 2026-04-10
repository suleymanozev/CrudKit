using CrudKit.SourceGen.Diagnostics;
using CrudKit.SourceGen.Models;
using Xunit;

namespace CrudKit.SourceGen.Tests.Diagnostics;

public class EntityValidatorTests
{
    private static EntityMetadata MakeEntity(
        bool implementsIEntity = true,
        bool implementsISoftDeletable = false,
        bool implementsIMultiTenant = false,
        string resource = "test-tables") =>
        new EntityMetadata("Test", "TestNs", "TestNs.Test", resource,
            false, true, true, true, false, false, null,
            implementsIEntity, implementsISoftDeletable, implementsIMultiTenant, []);

    [Fact]
    public void CRUD001_MissingIEntity()
    {
        var entity = MakeEntity(implementsIEntity: false);
        var diags = EntityValidator.Validate(entity, null);
        Assert.Contains(diags, d => d.Id == "CRUD001");
    }

    [Fact]
    public void CRUD010_EmptyResourceName()
    {
        var entity = MakeEntity(resource: "");
        var diags = EntityValidator.Validate(entity, null);
        Assert.Contains(diags, d => d.Id == "CRUD010");
    }

    [Fact]
    public void ValidEntity_NoDiagnostics()
    {
        var entity = MakeEntity(implementsISoftDeletable: true, implementsIMultiTenant: true);
        var diags = EntityValidator.Validate(entity, null);
        Assert.Empty(diags);
    }
}
