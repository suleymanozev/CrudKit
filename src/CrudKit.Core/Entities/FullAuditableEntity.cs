using CrudKit.Core.Interfaces;

namespace CrudKit.Core.Entities;

/// <summary>
/// Entity with timestamps and soft-delete support.
/// </summary>
public abstract class FullAuditableEntity<TKey> : AuditableEntity<TKey>, ISoftDeletable
    where TKey : notnull
{
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
}

/// <summary>
/// Full auditable entity using the default <see cref="Guid"/> primary key.
/// </summary>
public abstract class FullAuditableEntity : FullAuditableEntity<Guid>, IEntity, IAuditableEntity { }

/// <summary>
/// Full auditable entity with navigation properties to the user who created/updated/deleted it.
/// </summary>
public abstract class FullAuditableEntityWithUser<TKey, TUser, TUserKey> : AuditableEntityWithUser<TKey, TUser, TUserKey>, ISoftDeletable
    where TKey : notnull
    where TUser : class
    where TUserKey : notnull
{
    public DateTime? DeletedAt { get; set; }
    public Guid? DeleteBatchId { get; set; }
    public TUserKey? DeletedById { get; set; }
    public TUser? DeletedBy { get; set; }
}

/// <summary>
/// Full auditable entity with user tracking, using the default <see cref="Guid"/> primary key
/// and <see cref="Guid"/> user key.
/// </summary>
public abstract class FullAuditableEntityWithUser<TUser> : FullAuditableEntityWithUser<Guid, TUser, Guid>
    where TUser : class { }
