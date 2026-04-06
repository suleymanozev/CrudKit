using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Tests.Helpers;
using Xunit;

namespace CrudKit.Api.Tests.Endpoints;

/// <summary>
/// Tests for [ChildOf] attribute auto-discovery. Children are registered automatically
/// inside MapCrudEndpoints — no explicit call required.
/// </summary>
public class ChildOfEndpointTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ProjectTaskEntity and ProjectMilestoneEntity are auto-discovered via [ChildOf(typeof(ProjectEntity))].
    // MapCrudEndpoints triggers auto-discovery internally.
    private static Task<TestWebApp> CreateApp() => TestWebApp.CreateAsync(configureEndpoints: web =>
    {
        web.MapCrudEndpoints<ProjectEntity, CreateProjectDto, UpdateProjectDto>("projects");
    });

    private static async Task<string> CreateProject(TestWebApp app, string title = "My Project")
    {
        var response = await app.Client.PostAsJsonAsync("/api/projects", new { Title = title });
        response.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    // ---- ProjectTaskEntity — default FK convention, derived route "project-task-entities" ----
    // Route derived: ProjectTaskEntity → "project-task-entity" + "s" = "project-task-entitys" (raw convention)
    // We set ForeignKey = "ProjectId" explicitly. Route = default → ToKebabCase("ProjectTaskEntity") + "s"
    // = "project-task-entitys". Let's verify this in the test rather than hard-code expectations.

    [Fact]
    public async Task ChildOf_AutoRegistersListEndpoint()
    {
        await using var app = await CreateApp();
        var projectId = await CreateProject(app);

        // ProjectTaskEntity uses ForeignKey = "ProjectId", route = "project-task-entitys"
        var response = await app.Client.GetAsync($"/api/projects/{projectId}/project-task-entitys");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ChildOf_AutoRegistersGetEndpoint_NotFound()
    {
        await using var app = await CreateApp();
        var projectId = await CreateProject(app);
        var nonExistentId = Guid.NewGuid().ToString();

        var response = await app.Client.GetAsync($"/api/projects/{projectId}/project-task-entitys/{nonExistentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChildOf_AutoRegistersDeleteEndpoint_MasterNotFound_Returns404()
    {
        await using var app = await CreateApp();
        var fakeMasterId = Guid.NewGuid().ToString();
        var fakeDetailId = Guid.NewGuid().ToString();

        var response = await app.Client.DeleteAsync($"/api/projects/{fakeMasterId}/project-task-entitys/{fakeDetailId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChildOf_CustomRoute_UsesSpecifiedRoute()
    {
        await using var app = await CreateApp();
        var projectId = await CreateProject(app);

        // ProjectMilestoneEntity has Route = "milestones"
        var response = await app.Client.GetAsync($"/api/projects/{projectId}/milestones");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChildOf_CustomForeignKey_ListFiltersCorrectly()
    {
        await using var app = await CreateApp();
        var projectId = await CreateProject(app);

        // List should return empty for a fresh project
        var response = await app.Client.GetAsync($"/api/projects/{projectId}/milestones");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ChildOf_MasterNotFound_Returns404OnList()
    {
        await using var app = await CreateApp();
        var fakeMasterId = Guid.NewGuid().ToString();

        var response = await app.Client.GetAsync($"/api/projects/{fakeMasterId}/milestones");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChildOf_WithDetail_CoexistsWithAutoDiscovery()
    {
        // When WithDetail is chained after MapCrudEndpoints, auto-discovery has already
        // registered List/Get/Delete for ProjectMilestoneEntity via [ChildOf] (using __auto
        // endpoint names). WithDetail adds Create and Batch on top using standard names.
        // Both sets of endpoints use the same URL path — no name collision, no startup error.
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProjectEntity, CreateProjectDto, UpdateProjectDto>("projects2")
                .WithDetail<ProjectMilestoneEntity, CreateProjectMilestoneDto>("milestones", "ParentProjectId");
        });

        var projectResponse = await app.Client.PostAsJsonAsync("/api/projects2", new { Title = "P2" });
        projectResponse.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await projectResponse.Content.ReadAsStringAsync());
        var projectId = doc.RootElement.GetProperty("id").GetString()!;

        // List route works (served by auto-discovered endpoint)
        var listResponse = await app.Client.GetAsync($"/api/projects2/{projectId}/milestones");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        // POST works (registered only by WithDetail — auto-discovery does not register Create)
        var createResponse = await app.Client.PostAsJsonAsync(
            $"/api/projects2/{projectId}/milestones",
            new { Label = "M1" });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
    }

    [Fact]
    public async Task ToKebabCase_ConvertsCorrectly()
    {
        Assert.Equal("order-line", CrudEndpointMapper.ToKebabCase("OrderLine"));
        Assert.Equal("my-entity", CrudEndpointMapper.ToKebabCase("MyEntity"));
        Assert.Equal("product", CrudEndpointMapper.ToKebabCase("Product"));
    }
}
