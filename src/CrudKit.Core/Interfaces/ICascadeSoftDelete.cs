namespace CrudKit.Core.Interfaces;

/// <summary>
/// Bu entity silindiğinde [CascadeSoftDelete] attribute'lu
/// navigation property'leri de soft-delete yapılır.
/// </summary>
public interface ICascadeSoftDelete : ISoftDeletable { }
