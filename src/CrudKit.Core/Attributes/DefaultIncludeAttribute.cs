namespace CrudKit.Core.Attributes;

/// <summary>
/// Navigation property'ye eklenir.
/// EfRepo.List ve FindById sorgularına otomatik EF Include eklenir.
/// Response serializasyonunda da bu property dahil edilir.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class DefaultIncludeAttribute : Attribute { }
