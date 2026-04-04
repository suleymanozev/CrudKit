namespace CrudKit.Core.Enums;

/// <summary>
/// Controls when a [DefaultInclude] navigation property is included.
/// All = included in both list and detail queries.
/// DetailOnly = included only in detail (FindById) queries.
/// </summary>
public enum IncludeScope
{
    All,
    DetailOnly
}
