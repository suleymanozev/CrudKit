namespace CrudKit.Core.Attributes;

/// <summary>
/// Enables convention-based permission checks for this entity.
/// Auto-generates {route}:read, {route}:create, {route}:update, {route}:delete permission checks.
/// Can be overridden by fluent Authorize() at the call site.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RequirePermissionsAttribute : Attribute { }
