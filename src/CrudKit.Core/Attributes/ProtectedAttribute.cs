namespace CrudKit.Core.Attributes;

/// <summary>
/// This field cannot be set via the API — ignored in both Create and Update requests.
/// Use for server-computed or system-managed fields (e.g. GrandTotal, Status).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ProtectedAttribute : Attribute { }
