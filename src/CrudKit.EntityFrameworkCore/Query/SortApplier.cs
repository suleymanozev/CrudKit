using System.Linq.Expressions;
using System.Reflection;
using CrudKit.Core.Attributes;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Applies ORDER BY from a sort string.
/// Format: "field1,-field2,field3" — '-' prefix = DESC, no prefix = ASC.
/// Unknown fields are silently ignored.
/// Default (null/empty): CreatedAt DESC.
/// </summary>
public static class SortApplier
{
    public static IQueryable<T> Apply<T>(IQueryable<T> query, string? sortString)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(sortString))
            return ApplyDefault(query);

        var fields = sortString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        IOrderedQueryable<T>? ordered = null;

        foreach (var field in fields)
        {
            var isDesc = field.StartsWith('-');
            var name = isDesc ? field[1..] : field;

            var prop = FindProperty(typeof(T), name);
            if (prop is null) continue;

            if (!IsSortable<T>(prop)) continue; // field is not sortable — skip silently

            ordered = ordered is null
                ? ApplyOrderBy(query, prop, isDesc)
                : ApplyThenBy(ordered, prop, isDesc);
        }

        return ordered ?? ApplyDefault(query);
    }

    // ---- Sortability check ----

    /// <summary>
    /// Resolves whether a property is sortable using 3-level precedence:
    /// 1. Property-level [NotSortable] / [Sortable]
    /// 2. Entity-level [NotSortable] / [Sortable]
    /// 3. Default: sortable
    /// </summary>
    private static bool IsSortable<T>(PropertyInfo prop)
    {
        // Property level takes precedence over entity level
        if (prop.GetCustomAttribute<NotSortableAttribute>() is not null) return false;
        if (prop.GetCustomAttribute<SortableAttribute>() is not null) return true;

        // Entity level
        if (typeof(T).GetCustomAttribute<NotSortableAttribute>() is not null) return false;
        if (typeof(T).GetCustomAttribute<SortableAttribute>() is not null) return true;

        // Default: all properties are sortable
        return true;
    }

    private static IQueryable<T> ApplyDefault<T>(IQueryable<T> query) where T : class
    {
        var createdAt = typeof(T).GetProperty("CreatedAt");
        if (createdAt is null) return query;
        return ApplyOrderBy(query, createdAt, descending: true);
    }

    private static IOrderedQueryable<T> ApplyOrderBy<T>(
        IQueryable<T> query, PropertyInfo prop, bool descending) where T : class
    {
        var keySelector = BuildKeySelector<T>(prop);
        return descending
            ? Queryable.OrderByDescending(query, (dynamic)keySelector)
            : Queryable.OrderBy(query, (dynamic)keySelector);
    }

    private static IOrderedQueryable<T> ApplyThenBy<T>(
        IOrderedQueryable<T> query, PropertyInfo prop, bool descending) where T : class
    {
        var keySelector = BuildKeySelector<T>(prop);
        return descending
            ? Queryable.ThenByDescending(query, (dynamic)keySelector)
            : Queryable.ThenBy(query, (dynamic)keySelector);
    }

    private static LambdaExpression BuildKeySelector<T>(PropertyInfo prop)
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        return Expression.Lambda(member, param);
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null) return prop;

        prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (prop is not null) return prop;

        var pascal = ToPascalCase(name);
        return type.GetProperty(pascal, BindingFlags.Public | BindingFlags.Instance)
            ?? type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p => string.Equals(p.Name, pascal, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToPascalCase(string snake)
    {
        if (!snake.Contains('_')) return snake;
        return string.Concat(snake.Split('_')
            .Select(seg => seg.Length == 0 ? "" : char.ToUpper(seg[0]) + seg[1..]));
    }
}
