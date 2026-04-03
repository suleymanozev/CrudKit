using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Core.Tests.Attributes;

public class AttributeMetadataTests
{
    [CrudEntity(Table = "test_users", SoftDelete = true, Audit = true)]
    private class TestUser : IEntity, ISoftDeletable
    {
        public string Id { get; set; } = "";
        [Required, MaxLength(50), Searchable, Unique]
        public string Username { get; set; } = "";
        [SkipResponse, Hashed]
        public string Password { get; set; } = "";
        [Protected]
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    [Fact]
    public void CrudEntityAttribute_ShouldBeReadable()
    {
        var attr = typeof(TestUser).GetCustomAttribute<CrudEntityAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("test_users", attr.Table);
        Assert.True(attr.SoftDelete);
        Assert.True(attr.Audit);
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
}
