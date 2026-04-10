namespace CrudKit.SourceGen.Models;

/// <summary>
/// Immutable snapshot of a single entity property, extracted from a Roslyn IPropertySymbol.
/// </summary>
internal sealed class PropertyMetadata
{
    public string Name { get; }

    /// <summary>Short type name used in generated code (e.g. "string", "int", "MyType").</summary>
    public string TypeName { get; }

    /// <summary>Fully-qualified type name including namespace.</summary>
    public string FullTypeName { get; }

    /// <summary>True when the property's declared type is nullable (T? or Nullable&lt;T&gt;).</summary>
    public bool IsNullable { get; }

    // Attribute flags
    public bool IsRequired { get; }
    public bool HasMaxLength { get; }
    public int MaxLength { get; }
    public bool HasRange { get; }
    public string RangeMin { get; }
    public string RangeMax { get; }
    public bool IsHashed { get; }
    public bool IsProtected { get; }
    public bool IsSkipUpdate { get; }
    public bool IsSkipResponse { get; }
    public bool IsUnique { get; }
    public bool IsSearchable { get; }

    /// <summary>True when the property was expanded from a [Flatten] value object.</summary>
    public bool IsFlatten { get; }

    public PropertyMetadata(
        string name,
        string typeName,
        string fullTypeName,
        bool isNullable,
        bool isRequired,
        bool hasMaxLength,
        int maxLength,
        bool hasRange,
        string rangeMin,
        string rangeMax,
        bool isHashed,
        bool isProtected,
        bool isSkipUpdate,
        bool isSkipResponse,
        bool isUnique,
        bool isSearchable,
        bool isFlatten = false)
    {
        Name = name;
        TypeName = typeName;
        FullTypeName = fullTypeName;
        IsNullable = isNullable;
        IsRequired = isRequired;
        HasMaxLength = hasMaxLength;
        MaxLength = maxLength;
        HasRange = hasRange;
        RangeMin = rangeMin;
        RangeMax = rangeMax;
        IsHashed = isHashed;
        IsProtected = isProtected;
        IsSkipUpdate = isSkipUpdate;
        IsSkipResponse = isSkipResponse;
        IsUnique = isUnique;
        IsSearchable = isSearchable;
        IsFlatten = isFlatten;
    }
}
