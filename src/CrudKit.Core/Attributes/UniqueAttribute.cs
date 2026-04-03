namespace CrudKit.Core.Attributes;

/// <summary>EfRepo creates a partial unique index for this property (compatible with soft delete).</summary>
[AttributeUsage(AttributeTargets.Property)]
public class UniqueAttribute : Attribute { }
