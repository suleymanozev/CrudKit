using CrudKit.Core.Enums;

namespace CrudKit.Core.Attributes;

/// <summary>
/// Applied to entity classes (not properties). Declares a navigation property
/// that should be auto-included in EF Core queries.
/// Use Scope to control whether the include applies to list queries, detail queries, or both.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class DefaultIncludeAttribute : Attribute
{
    public string NavigationProperty { get; }
    public IncludeScope Scope { get; set; } = IncludeScope.All;

    public DefaultIncludeAttribute(string navigationProperty)
    {
        NavigationProperty = navigationProperty;
    }
}
