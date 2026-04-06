namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks a DTO as the manually-written Update DTO for the specified entity type.
/// When present, SourceGen skips generating UpdateDto for that entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class UpdateDtoForAttribute : Attribute
{
    public Type EntityType { get; }
    public UpdateDtoForAttribute(Type entityType) => EntityType = entityType;
}
