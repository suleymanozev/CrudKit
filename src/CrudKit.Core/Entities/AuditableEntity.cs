using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Entity with automatic timestamp tracking (CreatedAt, UpdatedAt).
/// </summary>
public abstract class AuditableEntity<TKey> : Entity<TKey>, IAuditableEntity<TKey>
    where TKey : notnull
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Auditable entity using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class AuditableEntity : AuditableEntity<Guid>, IEntity, IAuditableEntity { }

/// <summary>
/// Auditable entity with navigation properties to the user who created/updated it.
/// </summary>
public abstract class AuditableEntityWithUser<TKey, TUser, TUserKey> : AuditableEntity<TKey>
    where TKey : notnull
    where TUser : class
    where TUserKey : notnull
{
    public TUserKey? CreatedById { get; set; }
    public TUser? CreatedBy { get; set; }
    public TUserKey? UpdatedById { get; set; }
    public TUser? UpdatedBy { get; set; }
}

/// <summary>
/// Auditable entity with user tracking, using the default <see cref="Guid"/> primary key
/// and <see cref="Guid"/> user key.
/// </summary>
public abstract class AuditableEntityWithUser<TUser> : AuditableEntityWithUser<Guid, TUser, Guid>
    where TUser : class { }
