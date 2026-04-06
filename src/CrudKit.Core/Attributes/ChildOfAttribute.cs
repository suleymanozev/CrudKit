namespace CrudKit.Core.Attributes;

/// <summary>
/// Declares this entity as a child (detail) of the specified parent entity.
/// Framework automatically generates detail endpoints under the parent route
/// when <c>.MapChildEndpoints()</c> is called on the parent's endpoint group.
/// Route defaults to entity name pluralized in kebab-case (e.g. OrderLine → "order-lines").
/// ForeignKey defaults to {ParentTypeName}Id convention (e.g. Order → "OrderId").
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ChildOfAttribute : Attribute
{
    /// <summary>The parent entity type this entity belongs to.</summary>
    public Type ParentType { get; }

    /// <summary>URL route segment for the detail endpoints. Default: derived from entity name in kebab-case.</summary>
    public string? Route { get; set; }

    /// <summary>FK property name on this entity. Default: {ParentTypeName}Id.</summary>
    public string? ForeignKey { get; set; }

    public ChildOfAttribute(Type parentType) => ParentType = parentType;
}
