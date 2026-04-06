namespace CrudKit.Core.Attributes;

/// <summary>
/// Requires authentication for all CRUD endpoints of this entity.
/// Can be overridden by fluent Authorize() at the call site.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RequireAuthAttribute : Attribute { }
