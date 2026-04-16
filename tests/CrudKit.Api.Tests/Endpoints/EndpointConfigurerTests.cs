using System.Net;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;

using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

/// <summary>
/// Configurer that adds a custom GET endpoint under the ProductEntity route group.
/// Discovered automatically by <see cref="CrudEndpointMapper.ApplyEndpointConfigurer{TEntity}"/>.
/// </summary>
public class ProductConfigurerForTest : IEndpointConfigurer<ProductEntity>
{
    public void Configure(CrudEndpointGroup<ProductEntity> group)
    {
        group.WithCustomEndpoints(g =>
        {
            g.MapGet("/hello", () => Results.Ok(new { message = "hello from configurer" }));
        });
    }
}

public class EndpointConfigurerTests
{
    [Fact]
    public async Task ApplyEndpointConfigurer_DiscoversAndAppliesConfigurer()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                var group = web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
                CrudEndpointMapper.ApplyEndpointConfigurer(group);
            });

        var response = await app.Client.GetAsync("/api/products/hello");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("hello from configurer", json);
    }

    [Fact]
    public async Task ApplyEndpointConfigurer_StandardCrudStillWorks()
    {
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                var group = web.MapCrudEndpoints<ProductEntity, CreateProductDto, UpdateProductDto>("products");
                CrudEndpointMapper.ApplyEndpointConfigurer(group);
            });

        // Standard CRUD list endpoint should still work
        var response = await app.Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
