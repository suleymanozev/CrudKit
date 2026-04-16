using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Api.Tests.ValueObjects;

[ValueObject]
public class Money
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "TRY";
}

[CrudEntity]
public class PricedItem : IAuditableEntity
{
    public Guid Id { get; set; }
    [Required] public string Name { get; set; } = "";
    [Flatten] public Money Price { get; set; } = new();
    public Money Tax { get; set; } = new(); // not flattened
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreatePricedItemDto
{
    [Required] public string Name { get; set; } = "";
    public decimal PriceAmount { get; set; }
    public string PriceCurrency { get; set; } = "TRY";
    public Money Tax { get; set; } = new(); // nested
}

public class UpdatePricedItemDto
{
    public string? Name { get; set; }
    public decimal? PriceAmount { get; set; }
    public string? PriceCurrency { get; set; }
}

public class FlattenTests
{
    [Fact]
    public async Task Create_FlattenedVO_MapsCorrectly()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<PricedItem, CreatePricedItemDto, UpdatePricedItemDto>("priced-items"));

        var response = await app.Client.PostAsJsonAsync("/api/priced-items",
            new { Name = "Widget", PriceAmount = 29.90, PriceCurrency = "USD" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Price should be stored as nested VO in entity, returned as nested in response
        var price = json.RootElement.GetProperty("price");
        Assert.Equal(29.90m, price.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", price.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task Update_FlattenedVO_PartialUpdate()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
                web.MapCrudEndpoints<PricedItem, CreatePricedItemDto, UpdatePricedItemDto>("priced-items"));

        var createResp = await app.Client.PostAsJsonAsync("/api/priced-items",
            new { Name = "Widget", PriceAmount = 29.90, PriceCurrency = "USD" });
        var created = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;

        // Update only amount, currency stays
        var updateResp = await app.Client.PutAsJsonAsync($"/api/priced-items/{id}",
            new { PriceAmount = 49.90 });

        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
        var price = updated.RootElement.GetProperty("price");
        Assert.Equal(49.90m, price.GetProperty("amount").GetDecimal());
        Assert.Equal("USD", price.GetProperty("currency").GetString()); // preserved
    }
}
