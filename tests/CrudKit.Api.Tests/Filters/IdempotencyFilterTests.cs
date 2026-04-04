using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Filters;
using CrudKit.Api.Tests.Helpers;
using CrudKit.Core.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CrudKit.Api.Tests.Filters;

public class IdempotencyFilterTests
{
    // ---- Helpers ----

    private static StringContent JsonBody(object value)
        => new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    private static HttpRequestMessage PostWithKey(string url, string idempotencyKey, object? body = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body != null ? JsonBody(body) : null
        };
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        return req;
    }

    // ---- Tests ----

    /// <summary>
    /// Two POST requests with the same Idempotency-Key should return the same response,
    /// and the second response must carry the X-Idempotency-Replayed: true header.
    /// </summary>
    [Fact]
    public async Task SameKey_ReturnsCachedResponse()
    {
        var callCount = 0;

        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapPost("/items", () =>
                {
                    callCount++;
                    return Results.Ok(new { id = "abc-123", count = callCount });
                }).AddEndpointFilter<IdempotencyFilter>();
            });

        // First request
        var resp1 = await app.Client.SendAsync(PostWithKey("/items", "key-same-1"));
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.False(resp1.Headers.Contains("X-Idempotency-Replayed"),
            "First response must not have the replayed header.");

        // Second request with identical key
        var resp2 = await app.Client.SendAsync(PostWithKey("/items", "key-same-1"));
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.True(resp2.Headers.Contains("X-Idempotency-Replayed"),
            "Second response must carry X-Idempotency-Replayed header.");
        Assert.Equal("true", resp2.Headers.GetValues("X-Idempotency-Replayed").First());

        // Handler must have been called only once
        Assert.Equal(1, callCount);
    }

    /// <summary>
    /// Two POST requests with different Idempotency-Keys must both be processed
    /// independently without replayed headers.
    /// </summary>
    [Fact]
    public async Task DifferentKey_ProcessesNormally()
    {
        var callCount = 0;

        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapPost("/items", () =>
                {
                    callCount++;
                    return Results.Ok(new { count = callCount });
                }).AddEndpointFilter<IdempotencyFilter>();
            });

        var resp1 = await app.Client.SendAsync(PostWithKey("/items", "key-diff-1"));
        var resp2 = await app.Client.SendAsync(PostWithKey("/items", "key-diff-2"));

        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.False(resp1.Headers.Contains("X-Idempotency-Replayed"));
        Assert.False(resp2.Headers.Contains("X-Idempotency-Replayed"));

        // Both requests must have been handled
        Assert.Equal(2, callCount);
    }

    /// <summary>
    /// A POST request without the Idempotency-Key header must be processed normally
    /// every time without any caching or replayed headers.
    /// </summary>
    [Fact]
    public async Task NoKey_ProcessesNormally()
    {
        var callCount = 0;

        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapPost("/items", () =>
                {
                    callCount++;
                    return Results.Ok(new { count = callCount });
                }).AddEndpointFilter<IdempotencyFilter>();
            });

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/items");
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/items");

        var resp1 = await app.Client.SendAsync(req1);
        var resp2 = await app.Client.SendAsync(req2);

        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        Assert.False(resp1.Headers.Contains("X-Idempotency-Replayed"));
        Assert.False(resp2.Headers.Contains("X-Idempotency-Replayed"));

        // Both must be executed
        Assert.Equal(2, callCount);
    }

    /// <summary>
    /// GET requests must pass through the idempotency filter unchanged even when
    /// the Idempotency-Key header is present (GET is idempotent by definition).
    /// </summary>
    [Fact]
    public async Task GetRequests_AreNotAffected()
    {
        var callCount = 0;

        await using var app = await TestWebApp.CreateAsync(
            currentUser: new FakeCurrentUser(),
            configureEndpoints: web =>
            {
                web.MapGet("/items", () =>
                {
                    callCount++;
                    return Results.Ok(new { count = callCount });
                }).AddEndpointFilter<IdempotencyFilter>();
            });

        var getKey = "key-get-1";

        var req1 = new HttpRequestMessage(HttpMethod.Get, "/items");
        req1.Headers.Add("Idempotency-Key", getKey);

        var req2 = new HttpRequestMessage(HttpMethod.Get, "/items");
        req2.Headers.Add("Idempotency-Key", getKey);

        var resp1 = await app.Client.SendAsync(req1);
        var resp2 = await app.Client.SendAsync(req2);

        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);

        // GET is skipped by the filter, so no replayed header should appear
        Assert.False(resp1.Headers.Contains("X-Idempotency-Replayed"));
        Assert.False(resp2.Headers.Contains("X-Idempotency-Replayed"));

        // Handler must have been called for both GETs
        Assert.Equal(2, callCount);
    }
}
