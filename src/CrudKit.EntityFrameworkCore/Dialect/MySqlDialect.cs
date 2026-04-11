using System.Linq.Expressions;
using CrudKit.EntityFrameworkCore.Concurrency;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// MySQL/MariaDB dialect. LIKE is case-insensitive by default (utf8mb4_general_ci collation).
/// Uses ON DUPLICATE KEY UPDATE for upserts. Schemas map to databases in MySQL.
/// </summary>
public class MySqlDialect : IDbDialect
{
    public bool SupportsSchemas => false;

    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // MySQL LIKE is case-insensitive with default collation — no ToLower needed
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var contains = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!,
            Expression.Constant(value));
        return query.Where(Expression.Lambda<Func<T, bool>>(contains, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var startsWith = Expression.Call(
            memberAccess,
            typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
            Expression.Constant(value));
        return query.Where(Expression.Lambda<Func<T, bool>>(startsWith, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var updateList = string.Join(", ", columns.Select(c => $"{c} = VALUES({c})"));
        return $"INSERT INTO {table} ({colList}) VALUES ({paramList}) " +
               $"ON DUPLICATE KEY UPDATE {updateList}";
    }

    public void ConfigureConcurrencyToken(ModelBuilder modelBuilder, Type entityType)
    {
        // MySQL: manual uint token — same as SQLite
        modelBuilder.Entity(entityType)
            .Property(nameof(IConcurrent.RowVersion))
            .IsConcurrencyToken();
    }
}
