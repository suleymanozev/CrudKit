using CrudKit.Core.Enums;

namespace CrudKit.Core.Models;

/// <summary>Bir entity üzerindeki işlem izni.</summary>
public class Permission
{
    public string Entity { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public PermScope Scope { get; set; }
}
