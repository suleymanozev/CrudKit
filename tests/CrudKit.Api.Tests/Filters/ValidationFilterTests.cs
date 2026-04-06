using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Filters;
using CrudKit.Api.Tests.Helpers;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

// ---- DTOs used in these tests ----

/// <summary>DTO with DataAnnotation constraints for the fallback validation path.</summary>
public class AnnotatedDto
{
    [Required] public string Name { get; set; } = string.Empty;
    [Range(1, 100)] public int Age { get; set; }
}

/// <summary>DTO used with a registered FluentValidation validator.</summary>
public class FluentDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
}

/// <summary>FluentValidation validator that enforces Name and Email rules.</summary>
public class FluentDtoValidator : AbstractValidator<FluentDto>
{
    public FluentDtoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithErrorCode("NAME_REQUIRED");
        RuleFor(x => x.Email)
            .NotEmpty().WithErrorCode("EMAIL_REQUIRED")
            .EmailAddress().WithErrorCode("EMAIL_FORMAT");
    }
}

public class ValidationFilterTests
{
    private static string? GetString(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetString() : null;

    private static int GetInt(JsonDocument doc, string property)
        => doc.RootElement.TryGetProperty(property, out var el) ? el.GetInt32() : 0;

    // ---- FluentValidation path ----

    [Fact]
    public async Task FluentValidation_MultipleErrors_Returns400WithAllErrors()
    {
        // Arrange: register FluentDtoValidator; post a DTO with both fields invalid
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services => services.AddScoped<IValidator<FluentDto>, FluentDtoValidator>(),
            configureEndpoints: web =>
            {
                web.MapPost("/validate", (FluentDto dto) => Results.Ok())
                   .AddEndpointFilter<ValidationFilter<FluentDto>>();
            });

        // Act: empty dto — Name and Email both fail
        var response = await app.Client.PostAsJsonAsync("/validate", new FluentDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);

        Assert.Equal(400, GetInt(doc, "status"));
        Assert.Equal("VALIDATION_ERROR", GetString(doc, "code"));

        // Details array must contain at least two entries (Name + Email)
        Assert.True(doc.RootElement.TryGetProperty("details", out var details));
        Assert.True(details.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task FluentValidation_ValidDto_PassesThrough()
    {
        // Arrange
        await using var app = await TestWebApp.CreateAsync(
            configureServices: services => services.AddScoped<IValidator<FluentDto>, FluentDtoValidator>(),
            configureEndpoints: web =>
            {
                web.MapPost("/validate", (FluentDto dto) => Results.Ok(new { ok = true }))
                   .AddEndpointFilter<ValidationFilter<FluentDto>>();
            });

        // Act: valid dto
        var response = await app.Client.PostAsJsonAsync("/validate", new FluentDto { Name = "Alice", Email = "alice@example.com" });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
    }

    // ---- DataAnnotation fallback path (no IValidator<T> registered) ----

    [Fact]
    public async Task DataAnnotation_InvalidDto_Returns400()
    {
        // Arrange: no IValidator<AnnotatedDto> registered — falls back to DataAnnotations
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapPost("/validate", (AnnotatedDto dto) => Results.Ok())
                   .AddEndpointFilter<ValidationFilter<AnnotatedDto>>();
            });

        // Act: Name missing, Age out of range
        var response = await app.Client.PostAsJsonAsync("/validate", new AnnotatedDto { Name = "", Age = 0 });

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, GetInt(doc, "status"));
        Assert.Equal("VALIDATION_ERROR", GetString(doc, "code"));
    }

    [Fact]
    public async Task DataAnnotation_ValidDto_PassesThrough()
    {
        // Arrange: no IValidator registered
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                web.MapPost("/validate", (AnnotatedDto dto) => Results.Ok(new { passed = true }))
                   .AddEndpointFilter<ValidationFilter<AnnotatedDto>>();
            });

        // Act: all constraints satisfied
        var response = await app.Client.PostAsJsonAsync("/validate", new AnnotatedDto { Name = "Bob", Age = 30 });

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public async Task NullArgument_SkipsValidation_PassesThrough()
    {
        // When no argument of type T is present in the context, filter should skip validation
        await using var app = await TestWebApp.CreateAsync(
            configureEndpoints: web =>
            {
                // Endpoint does NOT accept FluentDto — filter has nothing to validate
                web.MapGet("/no-arg", () => Results.Ok(new { skipped = true }))
                   .AddEndpointFilter<ValidationFilter<FluentDto>>();
            });

        var response = await app.Client.GetAsync("/no-arg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("skipped").GetBoolean());
    }
}
