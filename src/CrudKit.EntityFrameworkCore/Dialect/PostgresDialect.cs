using System.Linq.Expressions;
using System.Reflection;
using CrudKit.EntityFrameworkCore.Concurrency;
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

    private static readonly MethodInfo? _iLikeMethod = FindILikeMethod();

    private static MethodInfo? FindILikeMethod()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.GetName().Name?.Contains("Npgsql") == true) continue;

            foreach (var type in assembly.GetExportedTypes())
            {
                if (!type.Name.Contains("DbFunctions")) continue;
                var method = type.GetMethod("ILike",
                    [typeof(DbFunctions), typeof(string), typeof(string)]);
                if (method != null) return method;
            }
        }
        return null;
    }

    public IQueryable<T> ApplyLike<T>(
        IQueryable<T> query,
        Expression<Func<T, string>> property,
        string value) where T : class
    {
        if (_iLikeMethod == null)
            return _fallback.ApplyLike(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"%{value}%");
        var call = Expression.Call(
            _iLikeMethod,
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
        if (_iLikeMethod == null)
            return _fallback.ApplyStartsWith(query, property, value);

        var param = property.Parameters[0];
        var memberAccess = property.Body;
        var pattern = Expression.Constant($"{value}%");
        var call = Expression.Call(
            _iLikeMethod,
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

    public void ConfigureConcurrencyToken(ModelBuilder modelBuilder, Type entityType)
    {
        // PostgreSQL: manual uint token — works with all PostgreSQL setups.
        // For xmin-based concurrency, override OnModelCreatingCustom and call UseXminAsConcurrencyToken().
        modelBuilder.Entity(entityType)
            .Property(nameof(IConcurrent.RowVersion))
            .IsConcurrencyToken();
    }

}
