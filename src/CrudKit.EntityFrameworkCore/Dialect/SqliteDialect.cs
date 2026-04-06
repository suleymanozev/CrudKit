using System.Linq.Expressions;
using CrudKit.EntityFrameworkCore.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// SQLite dialect. Uses ToLowerInvariant() for case-insensitive operations since
/// SQLite LIKE is only ASCII case-insensitive by default.
/// SQLite supports the same ON CONFLICT syntax as PostgreSQL.
/// </summary>
public class SqliteDialect : IDbDialect
{
    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
        var contains = Expression.Call(
            toLower,
            typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
            Expression.Constant(value.ToLowerInvariant()));
        return query.Where(Expression.Lambda<Func<T, bool>>(contains, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var toLower = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.ToLowerInvariant), Type.EmptyTypes)!);
        var startsWith = Expression.Call(
            toLower,
            typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
            Expression.Constant(value.ToLowerInvariant()));
        return query.Where(Expression.Lambda<Func<T, bool>>(startsWith, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = EXCLUDED.{c}"));
        var keyList = string.Join(", ", keyColumns);
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) " +
               $"ON CONFLICT ({keyList}) DO UPDATE SET {updateList}";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => throw new NotSupportedException("SQLite does not support sequences. Use SequenceGenerator.");

    public void ConfigureConcurrencyToken(ModelBuilder modelBuilder, Type entityType)
    {
        // SQLite: manual uint token — app increments RowVersion on each save
        modelBuilder.Entity(entityType)
            .Property(nameof(IConcurrent.RowVersion))
            .IsConcurrencyToken();
    }
}
