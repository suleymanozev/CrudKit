namespace CrudKit.Core.Attributes;

/// <summary>
/// EfRepo.Create sırasında bu property'nin değeri BCrypt ile hash'lenir.
/// SkipResponse ile birlikte kullanılır (hash response'a çıkmaz).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class HashedAttribute : Attribute { }
