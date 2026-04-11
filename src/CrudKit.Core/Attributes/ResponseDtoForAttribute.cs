namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks a class as the response DTO for an entity type.
/// CrudKit will use this DTO for GET responses when an IResponseMapper is registered.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ResponseDtoForAttribute : Attribute
{
    public Type EntityType { get; }
    public ResponseDtoForAttribute(Type entityType) => EntityType = entityType;
}
