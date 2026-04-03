using System.Text.Json;
using CrudKit.Core.Models;
using CrudKit.Core.Serialization;
using Xunit;

namespace CrudKit.Core.Tests.Models;

public class OptionalTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new OptionalJsonConverterFactory() }
    };

    [Fact]
    public void Undefined_HasValue_IsFalse()
    {
        var opt = Optional<string>.Undefined;
        Assert.False(opt.HasValue);
        Assert.Null(opt.Value);
    }

    [Fact]
    public void From_HasValue_IsTrue()
    {
        var opt = Optional<string>.From("hello");
        Assert.True(opt.HasValue);
        Assert.Equal("hello", opt.Value);
    }

    [Fact]
    public void From_NullValue_HasValue_IsTrue()
    {
        var opt = Optional<string?>.From(null);
        Assert.True(opt.HasValue);
        Assert.Null(opt.Value);
    }

    [Fact]
    public void ImplicitConversion_ShouldSetHasValue()
    {
        Optional<int> opt = 42;
        Assert.True(opt.HasValue);
        Assert.Equal(42, opt.Value);
    }

    [Fact]
    public void Deserialize_PresentField_ShouldHaveValue()
    {
        var json = """{"Name":"test","Price":99}""";
        var dto = JsonSerializer.Deserialize<TestDto>(json, Options)!;
        Assert.True(dto.Name.HasValue);
        Assert.Equal("test", dto.Name.Value);
        Assert.True(dto.Price.HasValue);
        Assert.Equal(99m, dto.Price.Value);
    }

    [Fact]
    public void Deserialize_MissingField_ShouldBeUndefined()
    {
        var json = """{"Price":99}""";
        var dto = JsonSerializer.Deserialize<TestDto>(json, Options)!;
        Assert.False(dto.Name.HasValue);
        Assert.True(dto.Price.HasValue);
    }

    [Fact]
    public void Deserialize_ExplicitNull_ShouldHaveValue_AsNull()
    {
        var json = """{"Name":null,"Price":99}""";
        var dto = JsonSerializer.Deserialize<TestDto>(json, Options)!;
        Assert.True(dto.Name.HasValue);
        Assert.Null(dto.Name.Value);
    }

    [Fact]
    public void Serialize_PresentValue_ShouldWriteValue()
    {
        var dto = new TestDto { Name = Optional<string?>.From("test"), Price = Optional<decimal?>.From(99m) };
        var json = JsonSerializer.Serialize(dto, Options);
        Assert.Contains("\"Name\":\"test\"", json);
        Assert.Contains("\"Price\":99", json);
    }

    [Fact]
    public void Serialize_UndefinedValue_ShouldWriteNull()
    {
        var dto = new TestDto { Name = Optional<string?>.Undefined, Price = Optional<decimal?>.From(99m) };
        var json = JsonSerializer.Serialize(dto, Options);
        Assert.Contains("\"Name\":null", json);
        Assert.Contains("\"Price\":99", json);
    }

    private record TestDto
    {
        public Optional<string?> Name { get; init; }
        public Optional<decimal?> Price { get; init; }
    }
}
