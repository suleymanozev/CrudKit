namespace CrudKit.Core.Interfaces;

/// <summary>
/// Entity with automatic timestamp tracking.
/// CrudKitDbContext sets CreatedAt and UpdatedAt automatically on save.
/// </summary>
public interface IAuditableEntity<TKey> : IEntity<TKey> where TKey : notnull
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Auditable entity using the default <see cref="Guid"/> primary key.
/// </summary>
public interface IAuditableEntity : IAuditableEntity<Guid>, IEntity { }
