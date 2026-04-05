namespace CrudKit.Core.Interfaces;

/// <summary>
/// Implemented by child entities that participate in cascade soft-delete.
/// The parent entity must be decorated with [CascadeSoftDelete] attributes.
/// </summary>
public interface ICascadeSoftDelete<TParent> : ISoftDeletable
    where TParent : class, IAuditableEntity, ISoftDeletable
{
    static abstract string ParentForeignKey { get; }
}
