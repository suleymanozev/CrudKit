namespace CrudKit.Core.Interfaces;

/// <summary>
/// When this entity is deleted, navigation properties marked with
/// [CascadeSoftDelete] are also soft-deleted.
/// </summary>
public interface ICascadeSoftDelete : ISoftDeletable { }
