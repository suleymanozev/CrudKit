namespace CrudKit.Core.Attributes;

/// <summary>Excludes this property from JSON responses (e.g. password_hash).</summary>
[AttributeUsage(AttributeTargets.Property)]
public class SkipResponseAttribute : Attribute { }
