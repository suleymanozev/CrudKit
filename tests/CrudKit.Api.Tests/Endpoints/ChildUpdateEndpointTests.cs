using System.Net;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Attributes;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

/// <summary>
/// Update DTO for InvoiceLineEntity — used by both explicit WithChild and auto-discovery tests.
/// </summary>
[UpdateDtoFor(typeof(InvoiceLineEntity))]
public class UpdateInvoiceLineDto
{
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
}

/// <summary>
/// Tests for child entity Update endpoints — both explicit WithChild&lt;T,TCreate,TUpdate&gt;
/// and auto-discovered via [UpdateDtoFor].
/// </summary>
public class ChildUpdateEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task WithChild3TypeParams_UpdateEndpoint_Works()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices")
                    .WithChild<InvoiceLineEntity, CreateInvoiceLineDto, UpdateInvoiceLineDto>(
                        "lines", "InvoiceId");
            });

        // Create master
        var invoiceResp = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        invoiceResp.EnsureSuccessStatusCode();
        var invoiceJson = JsonDocument.Parse(await invoiceResp.Content.ReadAsStringAsync());
        var invoiceId = invoiceJson.RootElement.GetProperty("id").GetString()!;

        // Create child
        var lineResp = await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { InvoiceId = invoiceId, Description = "Widget", Amount = 100.0 });
        Assert.Equal(HttpStatusCode.Created, lineResp.StatusCode);
        var lineJson = JsonDocument.Parse(await lineResp.Content.ReadAsStringAsync());
        var lineId = lineJson.RootElement.GetProperty("id").GetString()!;

        // Update child
        var updateResp = await app.Client.PutAsJsonAsync($"/api/invoices/{invoiceId}/lines/{lineId}",
            new { Description = "Updated Widget", Amount = 200.0 });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
        Assert.Equal("Updated Widget", updated.RootElement.GetProperty("description").GetString());
        Assert.Equal(200.0, updated.RootElement.GetProperty("amount").GetDouble());
    }

    [Fact]
    public async Task ChildUpdate_WrongMasterId_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices")
                    .WithChild<InvoiceLineEntity, CreateInvoiceLineDto, UpdateInvoiceLineDto>(
                        "lines", "InvoiceId");
            });

        var invoiceResp = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        invoiceResp.EnsureSuccessStatusCode();
        var invoiceJson = JsonDocument.Parse(await invoiceResp.Content.ReadAsStringAsync());
        var invoiceId = invoiceJson.RootElement.GetProperty("id").GetString()!;

        var lineResp = await app.Client.PostAsJsonAsync($"/api/invoices/{invoiceId}/lines",
            new { InvoiceId = invoiceId, Description = "Widget", Amount = 100.0 });
        Assert.Equal(HttpStatusCode.Created, lineResp.StatusCode);
        var lineJson = JsonDocument.Parse(await lineResp.Content.ReadAsStringAsync());
        var lineId = lineJson.RootElement.GetProperty("id").GetString()!;

        // Try to update with wrong master ID — should 404 because master doesn't exist
        var wrongMasterId = Guid.NewGuid().ToString();
        var updateResp = await app.Client.PutAsJsonAsync($"/api/invoices/{wrongMasterId}/lines/{lineId}",
            new { Description = "Hacked" });
        Assert.Equal(HttpStatusCode.NotFound, updateResp.StatusCode);
    }

    [Fact]
    public async Task ChildUpdate_NonExistentChild_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices")
                    .WithChild<InvoiceLineEntity, CreateInvoiceLineDto, UpdateInvoiceLineDto>(
                        "lines", "InvoiceId");
            });

        var invoiceResp = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        invoiceResp.EnsureSuccessStatusCode();
        var invoiceJson = JsonDocument.Parse(await invoiceResp.Content.ReadAsStringAsync());
        var invoiceId = invoiceJson.RootElement.GetProperty("id").GetString()!;

        var fakeChildId = Guid.NewGuid().ToString();
        var updateResp = await app.Client.PutAsJsonAsync($"/api/invoices/{invoiceId}/lines/{fakeChildId}",
            new { Description = "Ghost" });
        Assert.Equal(HttpStatusCode.NotFound, updateResp.StatusCode);
    }

    [Fact]
    public async Task ChildUpdate_BelongsToDifferentMaster_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<InvoiceEntity, CreateInvoiceDto, UpdateInvoiceDto>("invoices")
                    .WithChild<InvoiceLineEntity, CreateInvoiceLineDto, UpdateInvoiceLineDto>(
                        "lines", "InvoiceId");
            });

        // Create two masters
        var inv1Resp = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-001" });
        inv1Resp.EnsureSuccessStatusCode();
        var inv1Id = JsonDocument.Parse(await inv1Resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        var inv2Resp = await app.Client.PostAsJsonAsync("/api/invoices", new { Title = "INV-002" });
        inv2Resp.EnsureSuccessStatusCode();
        var inv2Id = JsonDocument.Parse(await inv2Resp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Create child under invoice 1
        var lineResp = await app.Client.PostAsJsonAsync($"/api/invoices/{inv1Id}/lines",
            new { InvoiceId = inv1Id, Description = "Widget", Amount = 50.0 });
        Assert.Equal(HttpStatusCode.Created, lineResp.StatusCode);
        var lineId = JsonDocument.Parse(await lineResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Try to update the child via invoice 2 — FK mismatch should return 404
        var updateResp = await app.Client.PutAsJsonAsync($"/api/invoices/{inv2Id}/lines/{lineId}",
            new { Description = "Hacked" });
        Assert.Equal(HttpStatusCode.NotFound, updateResp.StatusCode);
    }

    [Fact]
    public async Task AutoDiscoveredUpdate_WithUpdateDtoFor_Works()
    {
        // ProjectMilestoneEntity has [ChildOf(typeof(ProjectEntity), Route="milestones", FK="ParentProjectId")].
        // CreateProjectMilestoneDto does NOT have [CreateDtoFor], so POST is not auto-discovered.
        // We chain WithChild to register POST, then verify that the auto-discovered
        // [UpdateDtoFor(typeof(ProjectMilestoneEntity))] registers a PUT endpoint.
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProjectEntity, CreateProjectDto, UpdateProjectDto>("projects")
                    .WithChild<ProjectMilestoneEntity, CreateProjectMilestoneDto>("milestones", "ParentProjectId");
            });

        // Create master
        var projectResp = await app.Client.PostAsJsonAsync("/api/projects", new { Title = "My Project" });
        projectResp.EnsureSuccessStatusCode();
        var projectId = JsonDocument.Parse(await projectResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Create child milestone via WithChild-registered POST
        var createResp = await app.Client.PostAsJsonAsync($"/api/projects/{projectId}/milestones",
            new { Label = "v1.0" });
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var milestoneId = JsonDocument.Parse(await createResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;

        // Update child milestone via auto-discovered PUT from [UpdateDtoFor]
        var updateResp = await app.Client.PutAsJsonAsync($"/api/projects/{projectId}/milestones/{milestoneId}",
            new { Label = "v2.0" });
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        var updated = JsonDocument.Parse(await updateResp.Content.ReadAsStringAsync());
        Assert.Equal("v2.0", updated.RootElement.GetProperty("label").GetString());
    }
}

/// <summary>
/// Update DTO for ProjectMilestoneEntity — auto-discovered via [UpdateDtoFor].
/// </summary>
[UpdateDtoFor(typeof(ProjectMilestoneEntity))]
public class UpdateProjectMilestoneDto
{
    public string? Label { get; set; }
}
