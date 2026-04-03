namespace CrudKit.Core.Attributes;

/// <summary>EfRepo partial unique index oluşturur (soft delete ile uyumlu).</summary>
[AttributeUsage(AttributeTargets.Property)]
public class UniqueAttribute : Attribute { }
