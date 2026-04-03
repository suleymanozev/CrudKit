namespace CrudKit.Core.Attributes;

/// <summary>Includes this property in global search queries.</summary>
[AttributeUsage(AttributeTargets.Property)]
public class SearchableAttribute : Attribute { }
