namespace CrudKit.Core.Attributes;

/// <summary>
/// Applied to navigation properties. When the parent entity (ICascadeSoftDelete) is deleted,
/// this collection is also soft-deleted.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class CascadeSoftDeleteAttribute : Attribute { }
