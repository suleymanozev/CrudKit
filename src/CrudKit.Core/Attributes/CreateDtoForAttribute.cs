namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks a DTO as the manually-written Create DTO for the specified entity type.
/// When present, SourceGen skips generating CreateDto for that entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class CreateDtoForAttribute : Attribute
{
    public Type EntityType { get; }
    public CreateDtoForAttribute(Type entityType) => EntityType = entityType;
}
