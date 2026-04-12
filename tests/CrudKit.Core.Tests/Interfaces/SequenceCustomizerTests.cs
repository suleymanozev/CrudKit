using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Interfaces;

public class SequenceCustomizerTests
{
    private class TestEntity { }

    private class TestCustomizer : ISequenceCustomizer<TestEntity>
    {
        public string? ResolveTemplate(string? tenantId) => $"TEST-{{year}}-{{seq:4}}";
        public Dictionary<string, string>? ResolvePlaceholders(string? tenantId)
            => new() { ["branch"] = "HQ" };
    }

    private class DefaultCustomizer : ISequenceCustomizer<TestEntity> { }

    [Fact]
    public void Customizer_ResolveTemplate_ReturnsCustom()
    {
        var customizer = new TestCustomizer();
        Assert.Equal("TEST-{year}-{seq:4}", customizer.ResolveTemplate("tenant-1"));
    }

    [Fact]
    public void Customizer_ResolvePlaceholders_ReturnsDictionary()
    {
        var customizer = new TestCustomizer();
        var placeholders = customizer.ResolvePlaceholders("tenant-1");
        Assert.NotNull(placeholders);
        Assert.Equal("HQ", placeholders!["branch"]);
    }

    [Fact]
    public void DefaultCustomizer_ResolveTemplate_ReturnsNull()
    {
        ISequenceCustomizer<TestEntity> customizer = new DefaultCustomizer();
        Assert.Null(customizer.ResolveTemplate("tenant-1"));
    }

    [Fact]
    public void DefaultCustomizer_ResolvePlaceholders_ReturnsNull()
    {
        ISequenceCustomizer<TestEntity> customizer = new DefaultCustomizer();
        Assert.Null(customizer.ResolvePlaceholders("tenant-1"));
    }
}
