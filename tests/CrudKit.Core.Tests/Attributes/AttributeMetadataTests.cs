using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class AttributeMetadataTests
{
    [CrudEntity(Resource = "test_users")]
    private class TestUser : IAuditableEntity, ISoftDeletable
    {
        public Guid Id { get; set; }
        [Required, MaxLength(50), Searchable, Unique]
        public string Username { get; set; } = "";
        [SkipResponse, Hashed]
        public string Password { get; set; } = "";
        [Protected]
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeleteBatchId { get; set; }
    }

    [Fact]
    public void CrudEntityAttribute_ShouldBeReadable()
    {
        var attr = typeof(TestUser).GetCustomAttribute<CrudEntityAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("test_users", attr.Resource);
    }

    [Fact]
    public void SearchableAttribute_ShouldBeOnCorrectProperties()
    {
        var searchable = typeof(TestUser).GetProperties()
            .Where(p => p.GetCustomAttribute<SearchableAttribute>() != null)
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("Username", searchable);
        Assert.DoesNotContain("Password", searchable);
    }

    [Fact]
    public void SkipResponseAttribute_ShouldBeOnPassword()
    {
        var prop = typeof(TestUser).GetProperty("Password");
        Assert.NotNull(prop?.GetCustomAttribute<SkipResponseAttribute>());
    }

    [Fact]
    public void ProtectedAttribute_ShouldBeOnStatus()
    {
        var prop = typeof(TestUser).GetProperty("Status");
        Assert.NotNull(prop?.GetCustomAttribute<ProtectedAttribute>());
    }

    [Fact]
    public void UniqueAttribute_ShouldBeOnUsername()
    {
        var prop = typeof(TestUser).GetProperty("Username");
        Assert.NotNull(prop?.GetCustomAttribute<UniqueAttribute>());
    }

    [Fact]
    public void HashedAttribute_ShouldBeOnPassword()
    {
        var prop = typeof(TestUser).GetProperty("Password");
        Assert.NotNull(prop?.GetCustomAttribute<HashedAttribute>());
    }

    [Fact]
    public void TestUser_ShouldImplementISoftDeletable()
    {
        Assert.True(typeof(ISoftDeletable).IsAssignableFrom(typeof(TestUser)));
    }

    // --- Operation control tests ---

    [Fact]
    public void DefaultValues_AllEnabled()
    {
        // Default attribute has no ReadOnly, all Enable* flags are true
        var attr = new CrudEntityAttribute();
        Assert.False(attr.ReadOnly);
        Assert.True(attr.EnableCreate);
        Assert.True(attr.EnableUpdate);
        Assert.True(attr.EnableDelete);
        Assert.False(attr.EnableBulkDelete);
        Assert.True(attr.IsCreateEnabled);
        Assert.True(attr.IsUpdateEnabled);
        Assert.True(attr.IsDeleteEnabled);
    }

    [Fact]
    public void ReadOnly_DisablesAllMutations()
    {
        // ReadOnly=true must make all mutation computed props return false
        var attr = new CrudEntityAttribute { ReadOnly = true };
        Assert.False(attr.IsCreateEnabled);
        Assert.False(attr.IsUpdateEnabled);
        Assert.False(attr.IsDeleteEnabled);
    }

    [Fact]
    public void EnableCreateFalse_OnlyDisablesCreate()
    {
        // Only EnableCreate=false; Update and Delete remain enabled
        var attr = new CrudEntityAttribute { EnableCreate = false };
        Assert.False(attr.IsCreateEnabled);
        Assert.True(attr.IsUpdateEnabled);
        Assert.True(attr.IsDeleteEnabled);
    }

    [Fact]
    public void ReadOnly_OverridesEnableFlags()
    {
        // Even with EnableCreate=true explicitly, ReadOnly=true must win
        var attr = new CrudEntityAttribute { ReadOnly = true, EnableCreate = true };
        Assert.False(attr.IsCreateEnabled);
    }
}
