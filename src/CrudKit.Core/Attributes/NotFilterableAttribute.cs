namespace CrudKit.Core.Attributes;

/// <summary>
/// Prevents filtering on a property or an entire entity class.
/// When placed on a class, all properties are non-filterable unless a specific
/// property is annotated with <see cref="FilterableAttribute"/> (property wins).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class NotFilterableAttribute : Attribute { }
