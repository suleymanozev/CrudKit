namespace CrudKit.Core.Attributes;

/// <summary>Bu property Update işleminde güncellenmez — sadece Create sırasında set edilir.</summary>
[AttributeUsage(AttributeTargets.Property)]
public class SkipUpdateAttribute : Attribute { }
