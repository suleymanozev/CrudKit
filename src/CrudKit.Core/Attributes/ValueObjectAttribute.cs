namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks a class as a value object. Properties of this type on entities
/// can be flattened into DTOs when combined with [Flatten].
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ValueObjectAttribute : Attribute { }
