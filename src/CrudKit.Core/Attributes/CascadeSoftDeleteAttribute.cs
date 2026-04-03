namespace CrudKit.Core.Attributes;

/// <summary>
/// Navigation property'ye eklenir. Üst entity (ICascadeSoftDelete) silindiğinde
/// bu koleksiyon da soft-delete yapılır.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class CascadeSoftDeleteAttribute : Attribute { }
