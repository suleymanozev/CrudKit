namespace CrudKit.Core.Attributes;

/// <summary>
/// Per-operation auth on entity. Specify which operation and what role is required.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class AuthorizeOperationAttribute : Attribute
{
    public string Operation { get; }  // "Read", "Create", "Update", "Delete", "Export", "Import"
    public string Role { get; }

    public AuthorizeOperationAttribute(string operation, string role)
    {
        Operation = operation;
        Role = role;
    }
}
