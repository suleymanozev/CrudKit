namespace CrudKit.Core.Attributes;

/// <summary>
/// Prevents sorting on a property or an entire entity class.
/// When placed on a class, all properties are non-sortable unless a specific
/// property is annotated with <see cref="SortableAttribute"/> (property wins).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class NotSortableAttribute : Attribute { }
