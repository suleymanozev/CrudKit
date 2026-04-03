namespace CrudKit.Core.Attributes;

/// <summary>
/// The property value is BCrypt-hashed during EfRepo.Create.
/// Typically combined with SkipResponse so the hash is never exposed in responses.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class HashedAttribute : Attribute { }
