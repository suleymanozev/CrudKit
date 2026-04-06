using System.Net;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Helpers;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using Xunit;

namespace CrudKit.Api.Tests.Helpers;

/// <summary>
/// Tests for the 3-level feature flag resolution:
/// Global flag > Entity [NotX] > Entity [X] > Global flag fallback.
/// </summary>
public class FeatureResolverTests
{
    // ---- Unit tests: FeatureResolver logic ----

    [Fact]
    public void IsExportEnabled_GlobalOn_NoAttribute_ReturnsTrue()
    {
        Assert.True(FeatureResolver.IsExportEnabled<NoFlagEntity>(globalFlag: true));
    }

    [Fact]
    public void IsExportEnabled_GlobalOff_NoAttribute_ReturnsFalse()
    {
        Assert.False(FeatureResolver.IsExportEnabled<NoFlagEntity>(globalFlag: false));
    }

    [Fact]
    public void IsExportEnabled_GlobalOn_NotExportableClass_ReturnsFalse()
    {
        // [NotExportable] on class overrides global ON
        Assert.False(FeatureResolver.IsExportEnabled<OptOutEntity>(globalFlag: true));
    }

    [Fact]
    public void IsExportEnabled_GlobalOff_ExportableClass_ReturnsTrue()
    {
        // [Exportable] on class overrides global OFF
        Assert.True(FeatureResolver.IsExportEnabled<ProductEntity>(globalFlag: false));
    }

    [Fact]
    public void IsImportEnabled_GlobalOn_NoAttribute_ReturnsTrue()
    {
        Assert.True(FeatureResolver.IsImportEnabled<NoFlagEntity>(globalFlag: true));
    }

    [Fact]
    public void IsImportEnabled_GlobalOff_NoAttribute_ReturnsFalse()
    {
        Assert.False(FeatureResolver.IsImportEnabled<NoFlagEntity>(globalFlag: false));
    }

    [Fact]
    public void IsImportEnabled_GlobalOn_NotImportableClass_ReturnsFalse()
    {
        // [NotImportable] on class overrides global ON
        Assert.False(FeatureResolver.IsImportEnabled<OptOutEntity>(globalFlag: true));
    }

    [Fact]
    public void IsImportEnabled_GlobalOff_ImportableClass_ReturnsTrue()
    {
        // [Importable] on class overrides global OFF
        Assert.True(FeatureResolver.IsImportEnabled<ProductEntity>(globalFlag: false));
    }

    [Fact]
    public void IsAuditEnabled_GlobalOn_NoAttribute_ReturnsTrue()
    {
        Assert.True(FeatureResolver.IsAuditEnabled<NoFlagEntity>(globalFlag: true));
    }

    [Fact]
    public void IsAuditEnabled_GlobalOff_NoAttribute_ReturnsFalse()
    {
        Assert.False(FeatureResolver.IsAuditEnabled<NoFlagEntity>(globalFlag: false));
    }

    [Fact]
    public void IsAuditEnabled_GlobalOff_AuditedClass_ReturnsTrue()
    {
        // [Audited] on class overrides global OFF (entity-level opt-in)
        Assert.True(FeatureResolver.IsAuditEnabled<AuditedEntity>(globalFlag: false));
    }

    [Fact]
    public void IsAuditEnabled_GlobalOn_NotAuditedClass_ReturnsFalse()
    {
        // [NotAudited] on class overrides global ON
        Assert.False(FeatureResolver.IsAuditEnabled<NotAuditedEntity>(globalFlag: true));
    }

    // ---- Integration tests: endpoint existence via HTTP ----

    [Fact]
    public async Task Export_GlobalOn_NoAttribute_EndpointExists()
    {
        // UseExport() is on, NoFlagEntity has no attribute → export endpoint should exist
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: o => o.UseExport(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<NoFlagEntity, CreateNoFlagDto, UpdateNoFlagDto>("noflag");
            });

        var response = await app.Client.GetAsync("/api/noflag/export?format=csv");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_GlobalOff_NoAttribute_EndpointMissing()
    {
        // UseExport() is NOT on, NoFlagEntity has no attribute → export endpoint should not exist
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<NoFlagEntity, CreateNoFlagDto, UpdateNoFlagDto>("noflag");
            });

        var response = await app.Client.GetAsync("/api/noflag/export?format=csv");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_GlobalOn_NotExportableClass_EndpointMissing()
    {
        // UseExport() is on but OptOutEntity has [NotExportable] → endpoint must NOT exist
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: o => o.UseExport(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<OptOutEntity, CreateOptOutDto, UpdateOptOutDto>("optout");
            });

        var response = await app.Client.GetAsync("/api/optout/export?format=csv");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Export_GlobalOff_ExportableClass_EndpointExists()
    {
        // UseExport() is NOT on, but ProductEntity has [Exportable] → endpoint should exist
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        var response = await app.Client.GetAsync("/api/products/export?format=csv");
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_GlobalOn_NoAttribute_EndpointExists()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: o => o.UseImport(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<NoFlagEntity, CreateNoFlagDto, UpdateNoFlagDto>("noflag");
            });

        // Posting with no file should return 400 (endpoint exists)
        using var emptyContent = new System.Net.Http.MultipartFormDataContent();
        var response = await app.Client.PostAsync("/api/noflag/import", emptyContent);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Import_GlobalOff_NoAttribute_EndpointMissing()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<NoFlagEntity, CreateNoFlagDto, UpdateNoFlagDto>("noflag");
            });

        using var emptyContent = new System.Net.Http.MultipartFormDataContent();
        var response = await app.Client.PostAsync("/api/noflag/import", emptyContent);
        // POST /import doesn't exist; ASP.NET matches /{id} GET route → 405 Method Not Allowed
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Import_GlobalOn_NotImportableClass_EndpointMissing()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureOptions: o => o.UseImport(),
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<OptOutEntity, CreateOptOutDto, UpdateOptOutDto>("optout");
            });

        using var emptyContent = new System.Net.Http.MultipartFormDataContent();
        var response = await app.Client.PostAsync("/api/optout/import", emptyContent);
        // POST /import doesn't exist; ASP.NET matches /{id} GET route → 405 Method Not Allowed
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Import_GlobalOff_ImportableClass_EndpointExists()
    {
        // ProductEntity has [Importable] → endpoint exists even without UseImport()
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
            });

        using var emptyContent = new System.Net.Http.MultipartFormDataContent();
        var response = await app.Client.PostAsync("/api/products/import", emptyContent);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}

// ---- Helper entities for audit FeatureResolver unit tests ----

[Audited]
file class AuditedEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[NotAudited]
file class NotAuditedEntity : IAuditableEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
