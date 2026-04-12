using CrudKit.EntityFrameworkCore.Sequencing;
using Xunit;

namespace CrudKit.EntityFrameworkCore.Tests.Sequencing;

public class SequenceGeneratorTests
{
    [Theory]
    [InlineData("INV-{year}-{seq:5}", "INV-2026-", 5)]
    [InlineData("SO-{year}{month}-{seq:4}", "SO-202604-", 4)]
    [InlineData("WB-{seq:3}", "WB-", 3)]
    [InlineData("{year}/{month}/{day}-{seq:6}", "2026/04/09-", 6)]
    [InlineData("PAY-{seq:8}", "PAY-", 8)]
    public void ResolvePrefix_ParsesTemplateCorrectly(string template, string expectedPrefix, int expectedPadding)
    {
        var now = new DateOnly(2026, 4, 9);
        var (prefix, padding) = SequenceGenerator.ResolvePrefix(template, now);
        Assert.Equal(expectedPrefix, prefix);
        Assert.Equal(expectedPadding, padding);
    }

    [Theory]
    [InlineData("INV-2026-", 1, 5, "INV-2026-00001")]
    [InlineData("INV-2026-", 42, 5, "INV-2026-00042")]
    [InlineData("SO-202604-", 1, 4, "SO-202604-0001")]
    [InlineData("WB-", 999, 3, "WB-999")]
    [InlineData("PAY-", 1, 8, "PAY-00000001")]
    public void FormatSequenceValue_FormatsCorrectly(string prefix, long value, int padding, string expected)
    {
        var result = SequenceGenerator.FormatSequenceValue(prefix, value, padding);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolvePrefix_NoSeqToken_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SequenceGenerator.ResolvePrefix("INV-{year}", new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void ResolvePrefix_WithCustomPlaceholders()
    {
        var placeholders = new Dictionary<string, string> { ["prefix"] = "FTR" };
        var (prefix, padding) = SequenceGenerator.ResolvePrefix("{prefix}-{year}-{seq:5}", new DateOnly(2026, 4, 12), placeholders);
        Assert.Equal("FTR-2026-", prefix);
        Assert.Equal(5, padding);
    }

    [Fact]
    public void ResolvePrefix_WithMultiplePlaceholders()
    {
        var placeholders = new Dictionary<string, string> { ["branch"] = "IST", ["prefix"] = "INV" };
        var (prefix, padding) = SequenceGenerator.ResolvePrefix("{branch}-{prefix}-{year}{month}-{seq:4}", new DateOnly(2026, 4, 12), placeholders);
        Assert.Equal("IST-INV-202604-", prefix);
        Assert.Equal(4, padding);
    }

    [Fact]
    public void ResolvePrefix_NullPlaceholders_WorksAsDefault()
    {
        var (prefix, padding) = SequenceGenerator.ResolvePrefix("INV-{year}-{seq:5}", new DateOnly(2026, 4, 12), null);
        Assert.Equal("INV-2026-", prefix);
        Assert.Equal(5, padding);
    }
}
