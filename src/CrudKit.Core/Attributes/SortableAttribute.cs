namespace CrudKit.Core.Attributes;

/// <summary>
/// Marks an entity class or property as sortable.
/// At property level, overrides a [NotSortable] on the entity class.
/// At class level, explicitly opts the whole entity in (redundant when default is open,
/// but useful to document intent or to override a future global opt-out).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property)]
public class SortableAttribute : Attribute { }
