namespace CrudKit.Core.Attributes;

/// <summary>
/// Applied to a parent entity class to declare that when the parent is soft-deleted,
/// the specified child entities should also be cascade soft-deleted.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class CascadeSoftDeleteAttribute : Attribute
{
    public Type ChildType { get; }
    public string ForeignKeyProperty { get; }

    public CascadeSoftDeleteAttribute(Type childType, string foreignKeyProperty)
    {
        ChildType = childType;
        ForeignKeyProperty = foreignKeyProperty;
    }
}
