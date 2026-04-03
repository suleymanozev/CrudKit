namespace CrudKit.Core.Attributes;

/// <summary>Bu property JSON response'a dahil edilmez (ör: password_hash).</summary>
[AttributeUsage(AttributeTargets.Property)]
public class SkipResponseAttribute : Attribute { }
