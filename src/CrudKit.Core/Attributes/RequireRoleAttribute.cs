namespace CrudKit.Core.Attributes;

/// <summary>
/// Requires a specific role for all CRUD endpoints of this entity.
/// Can be overridden by fluent Authorize() at the call site.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class RequireRoleAttribute : Attribute
{
    public string Role { get; }
    public RequireRoleAttribute(string role) => Role = role;
}
