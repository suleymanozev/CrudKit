namespace CrudKit.Core.Attributes;

/// <summary>This property is not updated during Update operations — set only on Create.</summary>
[AttributeUsage(AttributeTargets.Property)]
public class SkipUpdateAttribute : Attribute { }
