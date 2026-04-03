namespace CrudKit.Core.Interfaces;

/// <summary>For entities that set DeletedAt instead of being physically removed.</summary>
public interface ISoftDeletable
{
    DateTime? DeletedAt { get; set; }
}
