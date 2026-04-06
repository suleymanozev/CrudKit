using CrudKit.Api.Services;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Api.Tests.Services;

/// <summary>
/// Tests for CsvImportService — covers quoted fields, empty values,
/// enum/Guid conversion, and empty rows.
/// </summary>
public class CsvImportServiceTests
{
    // Test entity with various property types
    public class CsvTestEntity : IAuditableEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public Guid? CategoryId { get; set; }
        public TestStatus Status { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public enum TestStatus { Active, Inactive, Archived }

    // Entity with [NotImportable] field
    public class NotImportableEntity : IAuditableEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        [NotImportable]
        public string Secret { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    [Fact]
    public void Parse_QuotedFieldWithComma_ParsesCorrectly()
    {
        var csv = "\"Name\",\"Price\"\n\"Widget, Large\",9.99";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal("Widget, Large", rows[0]["Name"]);
        Assert.Equal(9.99m, rows[0]["Price"]);
    }

    [Fact]
    public void Parse_EmptyValueForNullable_ReturnsNull()
    {
        var csv = "Name,Description\nWidget,";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal("Widget", rows[0]["Name"]);
        // Empty string for nullable type -> null
        Assert.False(rows[0].ContainsKey("Description") && rows[0]["Description"] != null);
    }

    [Fact]
    public void Parse_EmptyValueForValueType_ReturnsDefault()
    {
        var csv = "Name,Quantity\nWidget,";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal(0, rows[0]["Quantity"]);
    }

    [Fact]
    public void Parse_EnumValue_ConvertsCorrectly()
    {
        var csv = "Name,Status\nWidget,Active\nGadget,archived";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Equal(2, rows.Count);
        Assert.Equal(TestStatus.Active, rows[0]["Status"]);
        Assert.Equal(TestStatus.Archived, rows[1]["Status"]);
    }

    [Fact]
    public void Parse_GuidValue_ConvertsCorrectly()
    {
        var guidValue = Guid.NewGuid();
        var csv = $"Name,CategoryId\nWidget,{guidValue}";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal(guidValue, rows[0]["CategoryId"]);
    }

    [Fact]
    public void Parse_EmptyRowsBetweenData_SkipsEmptyRows()
    {
        var csv = "Name,Price\nWidget,9.99\n\n\nGadget,19.99";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Equal(2, rows.Count);
        Assert.Equal("Widget", rows[0]["Name"]);
        Assert.Equal("Gadget", rows[1]["Name"]);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyResults()
    {
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>("");
        Assert.Empty(rows);
        Assert.Empty(errors);
    }

    [Fact]
    public void Parse_InvalidConversion_AddsError()
    {
        var csv = "Name,Price\nWidget,not-a-number";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Single(errors);
        Assert.Equal("Price", errors[0].Field);
        Assert.Contains("Cannot convert", errors[0].Message);
    }

    [Fact]
    public void Parse_SystemFields_AreSkipped()
    {
        // Id, CreatedAt, UpdatedAt are system fields and should be ignored
        var csv = "Id,Name,CreatedAt\n00000000-0000-0000-0000-000000000001,Widget,2026-01-01";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal("Widget", rows[0]["Name"]);
        Assert.False(rows[0].ContainsKey("Id"));
        Assert.False(rows[0].ContainsKey("CreatedAt"));
    }

    [Fact]
    public void Parse_NotImportableField_IsSkipped()
    {
        var csv = "Name,Secret\nWidget,top-secret";
        var (rows, errors) = CsvImportService.Parse<NotImportableEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal("Widget", rows[0]["Name"]);
        Assert.False(rows[0].ContainsKey("Secret"));
    }

    [Fact]
    public void Parse_QuotedFieldWithEscapedQuote_ParsesCorrectly()
    {
        var csv = "Name,Description\n\"She said \"\"hello\"\"\",test";
        var (rows, errors) = CsvImportService.Parse<CsvTestEntity>(csv);

        Assert.Empty(errors);
        Assert.Single(rows);
        Assert.Equal("She said \"hello\"", rows[0]["Name"]);
    }
}
