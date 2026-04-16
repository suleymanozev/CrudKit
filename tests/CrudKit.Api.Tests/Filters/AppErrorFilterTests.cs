using System.Net;
using System.Text.Json;
using CrudKit.Api.Filters;
using CrudKit.Core.Models;

using Microsoft.EntityFrameworkCore;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class AppErrorFilterTests
{
    // Helper to read a JSON property from a JsonDocument
    private static string? GetString(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetString() : null;

    private static int GetInt(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetInt32() : 0;

    [Fact]
    public async Task ThrowsAppErrorNotFound_Returns404()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () => { throw AppError.NotFound(); })
               .AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, GetInt(doc, "status"));
        Assert.Equal("NOT_FOUND", GetString(doc, "code"));
    }

    [Fact]
    public async Task ThrowsAppErrorBadRequest_Returns400()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () => { throw AppError.BadRequest("bad input"); })
               .AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, GetInt(doc, "status"));
        Assert.Equal("BAD_REQUEST", GetString(doc, "code"));
    }

    [Fact]
    public async Task ThrowsAppErrorUnauthorized_Returns401()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () => { throw AppError.Unauthorized(); })
               .AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(401, GetInt(doc, "status"));
        Assert.Equal("UNAUTHORIZED", GetString(doc, "code"));
    }

    [Fact]
    public async Task ThrowsAppErrorForbidden_Returns403()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () => { throw AppError.Forbidden(); })
               .AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, GetInt(doc, "status"));
        Assert.Equal("FORBIDDEN", GetString(doc, "code"));
    }

    [Fact]
    public async Task ThrowsAppErrorConflict_Returns409()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () => { throw AppError.Conflict("already exists"); })
               .AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, GetInt(doc, "status"));
        Assert.Equal("CONFLICT", GetString(doc, "code"));
    }

    [Fact]
    public async Task ThrowsValidationError_Returns400WithErrors()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () =>
            {
                var errors = new ValidationErrors();
                errors.Add("name", "REQUIRED", "Name is required");
                errors.ThrowIfInvalid();
                return Results.Ok();
            }).AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, GetInt(doc, "status"));
        Assert.Equal("VALIDATION_ERROR", GetString(doc, "code"));
        Assert.True(doc.RootElement.TryGetProperty("details", out _));
    }

    [Fact]
    public async Task ThrowsDbUpdateConcurrencyException_Returns409()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () =>
            {
                throw new DbUpdateConcurrencyException("Concurrency error");
            }).AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, GetInt(doc, "status"));
        Assert.Equal("CONFLICT", GetString(doc, "code"));
    }

    [Fact]
    public async Task UnhandledException_Returns500_DevIncludesStackTrace()
    {
        await using var app = await TestWebApp.CreateAsync(
            environment: "Development",
            configureEndpoints: web =>
            {
                web.MapGet("/test", () =>
                {
                    throw new InvalidOperationException("something went wrong");
                }).AddEndpointFilter<AppErrorFilter>();
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, GetInt(doc, "status"));
        Assert.Equal("INTERNAL_ERROR", GetString(doc, "code"));

        // In Development the full exception string (including stack trace) is returned
        var message = GetString(doc, "message");
        Assert.NotNull(message);
        Assert.Contains("InvalidOperationException", message);
        Assert.Contains("something went wrong", message);
    }

    [Fact]
    public async Task UnhandledException_Returns500_ProdHidesStackTrace()
    {
        await using var app = await TestWebApp.CreateAsync(
            environment: "Production",
            configureEndpoints: web =>
            {
                web.MapGet("/test", () =>
                {
                    throw new InvalidOperationException("secret internal detail");
                }).AddEndpointFilter<AppErrorFilter>();
            });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(500, GetInt(doc, "status"));
        Assert.Equal("INTERNAL_ERROR", GetString(doc, "code"));

        // In Production the internal detail must not be leaked
        var message = GetString(doc, "message");
        Assert.NotNull(message);
        Assert.DoesNotContain("secret internal detail", message);
        Assert.Equal("An unexpected error occurred.", message);
    }

    [Fact]
    public async Task NormalResponse_PassesThrough()
    {
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapGet("/test", () => Results.Ok(new { value = 42 }))
               .AddEndpointFilter<AppErrorFilter>();
        });

        var response = await app.Client.GetAsync("/test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(42, doc.RootElement.GetProperty("value").GetInt32());
    }
}
