namespace CrudKit.Core.Attributes;

/// <summary>
/// Applied to navigation properties.
/// Automatically adds an EF Include to EfRepo.List and FindById queries.
/// The property is also included in response serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class DefaultIncludeAttribute : Attribute { }
