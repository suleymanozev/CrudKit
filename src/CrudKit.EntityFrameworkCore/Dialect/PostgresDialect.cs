using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Dialect;

/// <summary>
/// PostgreSQL dialect using EF.Functions.ILike for native case-insensitive search.
/// Requires Npgsql.EntityFrameworkCore.PostgreSQL to be loaded at runtime.
/// Falls back to GenericDialect if ILike is unavailable.
/// </summary>
public class PostgresDialect : IDbDialect
{
    private readonly GenericDialect _fallback = new();

    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        // Resolve NpgsqlDbFunctionsExtensions.ILike at runtime to avoid hard Npgsql dependency.
        var npgsqlType = Type.GetType(
            "NpgsqlEFCore.DbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL") ??
            Type.GetType(
            "Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL");

        var iLikeMethod = npgsqlType?.GetMethod("ILike",
            [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (iLikeMethod == null)
            return _fallback.ApplyLike(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var call = Expression.Call(
            iLikeMethod,
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
        var npgsqlType = Type.GetType(
            "NpgsqlEFCore.DbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL") ??
            Type.GetType(
            "Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL");

        var iLikeMethod = npgsqlType?.GetMethod("ILike",
            [typeof(DbFunctions), typeof(string), typeof(string)]);

        if (iLikeMethod == null)
            return _fallback.ApplyStartsWith(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"{value}%");
        var call = Expression.Call(
            iLikeMethod,
            Expression.Constant(EF.Functions),
            memberAccess,
            pattern);
        return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
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
        => $"SELECT nextval('{sequenceName}')";
}
