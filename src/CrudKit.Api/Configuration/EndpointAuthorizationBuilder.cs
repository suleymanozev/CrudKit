namespace CrudKit.Api.Configuration;

/// <summary>
/// Fluent builder for per-operation authorization on CRUD endpoints.
/// </summary>
public class EndpointAuthorizationBuilder
{
    /// <summary>Authorization config for Read (GET list, GET by id) operations.</summary>
    public OperationAuth Read { get; } = new();

    /// <summary>Authorization config for Create (POST) operations.</summary>
    public OperationAuth Create { get; } = new();

    /// <summary>Authorization config for Update (PUT, bulk-update) operations.</summary>
    public OperationAuth Update { get; } = new();

    /// <summary>Authorization config for Delete (DELETE, bulk-delete) operations.</summary>
    public OperationAuth Delete { get; } = new();

    /// <summary>Authorization config for Restore (POST /{id}/restore) operations.</summary>
    public OperationAuth Restore { get; } = new();

    /// <summary>Authorization config for Transition (POST /{id}/transition/{action}) operations.</summary>
    public OperationAuth Transition { get; } = new();

    /// <summary>Authorization config for Export (GET /export) operations.</summary>
    public OperationAuth Export { get; } = new();

    /// <summary>Authorization config for Import (POST /import) operations.</summary>
    public OperationAuth Import { get; } = new();

    private bool _useConventionPermissions;
    private string? _globalRole;

    /// <summary>Require a role for ALL operations.</summary>
    public void RequireRole(string role) => _globalRole = role;

    /// <summary>
    /// Auto-generate permission checks from entity route name.
    /// Maps to: {route}:read, {route}:create, {route}:update, {route}:delete, etc.
    /// </summary>
    public void RequirePermissions() => _useConventionPermissions = true;

    internal bool UseConventionPermissions => _useConventionPermissions;
    internal string? GlobalRole => _globalRole;
}

/// <summary>
/// Authorization config for a single CRUD operation (Read, Create, Update, etc.).
/// </summary>
public class OperationAuth
{
    internal string? Role { get; private set; }
    internal (string Entity, string Action)? Permission { get; private set; }

    /// <summary>Require a specific role for this operation.</summary>
    public void RequireRole(string role) => Role = role;

    /// <summary>Require a specific permission for this operation.</summary>
    public void RequirePermission(string entity, string action) => Permission = (entity, action);

    internal bool HasAuth => Role is not null || Permission is not null;
}
