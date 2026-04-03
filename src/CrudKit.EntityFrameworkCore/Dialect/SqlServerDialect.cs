using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// SQL Server dialect. Uses EF.Functions.Like (SQL Server default collation is case-insensitive).
/// Requires Microsoft.EntityFrameworkCore.SqlServer to be loaded at runtime.
/// Falls back to GenericDialect if unavailable.
/// </summary>
public class SqlServerDialect : IDbDialect
{
    private readonly GenericDialect _fallback = new();

    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            "Like", [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (likeMethod == null)
            return _fallback.ApplyLike(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var call = Expression.Call(
            likeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
    }

    public IQueryable<T> ApplyStartsWith<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        var likeMethod = typeof(DbFunctionsExtensions).GetMethod(
            "Like", [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (likeMethod == null)
            return _fallback.ApplyStartsWith(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"{value}%");
        var call = Expression.Call(
            likeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
    }

    public string GetUpsertSql(string table, string[] columns, string[] keyColumns)
    {
        var colList = string.Join(", ", columns);
        var updateList = string.Join(", ", columns.Select(c => $"target.{c} = source.{c}"));
        var onClause = string.Join(" AND ", keyColumns.Select(k => $"target.{k} = source.{k}"));
        var paramList = string.Join(", ", columns.Select((c, i) => $"@p{i} AS {c}"));
        return $"MERGE INTO {table} AS target " +
               $"USING (SELECT {paramList}) AS source " +
               $"ON {onClause} " +
               $"WHEN MATCHED THEN UPDATE SET {updateList} " +
               $"WHEN NOT MATCHED THEN INSERT ({colList}) VALUES ({string.Join(", ", columns.Select(c => $"source.{c}"))});";
    }

    public string GetSequenceNextValueSql(string sequenceName)
        => $"SELECT NEXT VALUE FOR {sequenceName}";
}
