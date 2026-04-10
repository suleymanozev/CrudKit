namespace CrudKit.Core.Attributes;

/// <summary>
/// When applied to a property whose type is a [ValueObject],
/// the DTO generators will flatten the value object's properties
/// using the pattern: {PropertyName}{VoPropertyName}.
/// Without this attribute, value object properties appear as nested objects in DTOs.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class FlattenAttribute : Attribute { }
