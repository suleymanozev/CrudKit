using System.Net;
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
    // Route derived: ProjectTaskEntity → "project-task-entity" + "s" = "project-task-entities" (raw convention)
    // We set ForeignKey = "ProjectId" explicitly. Route = default → ToKebabCase("ProjectTaskEntity") + "s"
    // = "project-task-entities". Let's verify this in the test rather than hard-code expectations.

    [Fact]
    public async Task ChildOf_AutoRegistersListEndpoint()
    {
        await using var app = await CreateApp();
        var projectId = await CreateProject(app);

        // ProjectTaskEntity uses ForeignKey = "ProjectId", route = "project-task-entities"
        var response = await app.Client.GetAsync($"/api/projects/{projectId}/project-task-entities");
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

        var response = await app.Client.GetAsync($"/api/projects/{projectId}/project-task-entities/{nonExistentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChildOf_AutoRegistersDeleteEndpoint_MasterNotFound_Returns404()
    {
        await using var app = await CreateApp();
        var fakeMasterId = Guid.NewGuid().ToString();
        var fakeDetailId = Guid.NewGuid().ToString();

        var response = await app.Client.DeleteAsync($"/api/projects/{fakeMasterId}/project-task-entities/{fakeDetailId}");
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
        // When WithChild is chained after MapCrudEndpoints, auto-discovery has already
        // registered List/Get/Delete for ProjectMilestoneEntity via [ChildOf] (using __auto
        // endpoint names). WithChild adds Create and Batch on top using standard names.
        // Both sets of endpoints use the same URL path — no name collision, no startup error.
        await using var app = await TestWebApp.CreateAsync(configureEndpoints: web =>
        {
            web.MapCrudEndpoints<ProjectEntity, CreateProjectDto, UpdateProjectDto>("projects2")
                .WithChild<ProjectMilestoneEntity, CreateProjectMilestoneDto>("milestones", "ParentProjectId");
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
    public async Task ChildOf_WithCreateDtoFor_RegistersPostEndpoint()
    {
        // ProjectTaskEntity has [ChildOf(typeof(ProjectEntity), ForeignKey = "ProjectId")]
        // CreateProjectTaskDto has [CreateDtoFor(typeof(ProjectTaskEntity))]
        // Auto-discovery should register POST under /api/projects/{masterId}/project-task-entities
        await using var app = await CreateApp();
        var projectId = await CreateProject(app, "Task Project");

        var createResponse = await app.Client.PostAsJsonAsync(
            $"/api/projects/{projectId}/project-task-entities",
            new { Name = "First Task" });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var doc = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        Assert.Equal("First Task", doc.RootElement.GetProperty("name").GetString());

        // FK should be auto-set to the master id
        var fk = doc.RootElement.GetProperty("projectId").GetString();
        Assert.Equal(projectId, fk);

        // Verify the item is retrievable via List
        var listResponse = await app.Client.GetAsync($"/api/projects/{projectId}/project-task-entities");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, listDoc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ChildOf_WithCreateDtoFor_MasterNotFound_Returns404()
    {
        await using var app = await CreateApp();
        var fakeMasterId = Guid.NewGuid().ToString();

        var response = await app.Client.PostAsJsonAsync(
            $"/api/projects/{fakeMasterId}/project-task-entities",
            new { Name = "Task" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChildOf_WithCreateDtoFor_ValidationFails_Returns400()
    {
        await using var app = await CreateApp();
        var projectId = await CreateProject(app, "Validation Project");

        // Name is [Required] — send empty string to trigger validation
        var response = await app.Client.PostAsJsonAsync(
            $"/api/projects/{projectId}/project-task-entities",
            new { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ToKebabCase_ConvertsCorrectly()
    {
        Assert.Equal("order-line", CrudEndpointMapper.ToKebabCase("OrderLine"));
        Assert.Equal("my-entity", CrudEndpointMapper.ToKebabCase("MyEntity"));
        Assert.Equal("product", CrudEndpointMapper.ToKebabCase("Product"));
    }
}
