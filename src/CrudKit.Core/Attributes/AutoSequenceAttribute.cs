namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks a string property for automatic sequence number generation.
/// The value is set automatically during entity creation (BeforeSave).
/// Template tokens: {year}, {month}, {day}, {seq:N} where N is zero-padding width.
/// Custom placeholders can be resolved via ISequenceCustomizer.
/// Sequences are scoped per tenant + entity type + resolved prefix.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class AutoSequenceAttribute : Attribute
{
    public string Template { get; }

    /// <param name="template">Sequence template. Default: "{seq:5}". Tokens: {year}, {month}, {day}, {seq:N}, custom placeholders.</param>
    public AutoSequenceAttribute(string template = "{seq:5}")
    {
        Template = template;
    }
}
