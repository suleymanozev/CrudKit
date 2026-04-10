using System.Linq.Expressions;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Models;
using CrudKit.EntityFrameworkCore.Dialect;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Converts a (propertyName, FilterOp) pair into a LINQ Where clause.
/// Property lookup is case-insensitive and supports snake_case → PascalCase conversion.
/// Unknown property names are silently ignored (SQL injection protection).
/// </summary>
public class FilterApplier
{
    private readonly IDbDialect _dialect;

    public FilterApplier(IDbDialect dialect) => _dialect = dialect;

    public IQueryable<T> Apply<T>(IQueryable<T> query, string propertyName, FilterOp op)
        where T : class
    {
        var prop = FindProperty(typeof(T), propertyName);
        if (prop is null) return query; // unknown field — skip silently

        if (!IsFilterable<T>(prop)) return query; // field is not filterable — skip silently

        return op.Operator switch
        {
            "like"    => ApplyLikeFilter(query, prop, op.Value),
            "starts"  => ApplyStartsFilter(query, prop, op.Value),
            "in"      => ApplyIn(query, prop, op.Values ?? []),
            "null"    => ApplyNull(query, prop),
            "notnull" => ApplyNotNull(query, prop),
            _         => ApplyComparison(query, prop, op),
        };
    }

    // ---- Filterability check ----

    /// <summary>
    /// Resolves whether a property is filterable using 3-level precedence:
    /// 1. Property-level [NotFilterable] / [Filterable]
    /// 2. Entity-level [NotFilterable] / [Filterable]
    /// 3. Default: filterable
    /// </summary>
    private static bool IsFilterable<T>(PropertyInfo prop)
    {
        // Property level takes precedence over entity level
        if (prop.GetCustomAttribute<NotFilterableAttribute>() is not null) return false;
        if (prop.GetCustomAttribute<FilterableAttribute>() is not null) return true;

        // Entity level
        if (typeof(T).GetCustomAttribute<NotFilterableAttribute>() is not null) return false;
        if (typeof(T).GetCustomAttribute<FilterableAttribute>() is not null) return true;

        // Default: all properties are filterable
        return true;
    }

    // ---- Property resolution ----

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        // 1. Exact match
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (prop is not null) return prop;

        // 2. Case-insensitive match
        prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (prop is not null) return prop;

        // 3. snake_case → PascalCase conversion (e.g. created_at → CreatedAt)
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

    // ---- Filter builders ----

    private IQueryable<T> ApplyLikeFilter<T>(IQueryable<T> query, PropertyInfo prop, string value)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var memberAccess = Expression.Property(param, prop);
        var stringExpr = Expression.Lambda<Func<T, string>>(memberAccess, param);
        return _dialect.ApplyLike(query, stringExpr, value);
    }

    private IQueryable<T> ApplyStartsFilter<T>(IQueryable<T> query, PropertyInfo prop, string value)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var memberAccess = Expression.Property(param, prop);
        var stringExpr = Expression.Lambda<Func<T, string>>(memberAccess, param);
        return _dialect.ApplyStartsWith(query, stringExpr, value);
    }

    private static IQueryable<T> ApplyIn<T>(IQueryable<T> query, PropertyInfo prop, List<string> values)
        where T : class
    {
        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);

        var propType = prop.PropertyType;

        if (propType == typeof(string))
        {
            var containsMethod = typeof(List<string>).GetMethod(nameof(List<string>.Contains))!;
            var constant = Expression.Constant(values);
            var call = Expression.Call(constant, containsMethod, member);
            return query.Where(Expression.Lambda<Func<T, bool>>(call, param));
        }

        // For numeric types — build OR chain
        var convertedValues = values
            .Select(v => ConvertValue(v, propType))
            .Where(v => v is not null)
            .ToList();

        Expression? orExpr = null;
        foreach (var converted in convertedValues)
        {
            var eq = Expression.Equal(member, Expression.Constant(converted, propType));
            orExpr = orExpr is null ? eq : Expression.OrElse(orExpr, eq);
        }

        if (orExpr is null) return query;
        return query.Where(Expression.Lambda<Func<T, bool>>(orExpr, param));
    }

    private static IQueryable<T> ApplyNull<T>(IQueryable<T> query, PropertyInfo prop)
        where T : class
    {
        // Value types cannot be null — skip this filter silently
        if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) is null)
            return query;

        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var nullConst = Expression.Constant(null, prop.PropertyType);
        var eq = Expression.Equal(member, nullConst);
        return query.Where(Expression.Lambda<Func<T, bool>>(eq, param));
    }

    private static IQueryable<T> ApplyNotNull<T>(IQueryable<T> query, PropertyInfo prop)
        where T : class
    {
        // Value types are never null — skip this filter silently
        if (prop.PropertyType.IsValueType && Nullable.GetUnderlyingType(prop.PropertyType) is null)
            return query;

        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var nullConst = Expression.Constant(null, prop.PropertyType);
        var neq = Expression.NotEqual(member, nullConst);
        return query.Where(Expression.Lambda<Func<T, bool>>(neq, param));
    }

    private static IQueryable<T> ApplyComparison<T>(IQueryable<T> query, PropertyInfo prop, FilterOp op)
        where T : class
    {
        var convertedValue = ConvertValue(op.Value, prop.PropertyType);
        if (convertedValue is null) return query;

        var param = Expression.Parameter(typeof(T), "e");
        var member = Expression.Property(param, prop);
        var constant = Expression.Constant(convertedValue, prop.PropertyType);

        Expression? body = op.Operator switch
        {
            "eq"  => Expression.Equal(member, constant),
            "neq" => Expression.NotEqual(member, constant),
            "gt"  => Expression.GreaterThan(member, constant),
            "gte" => Expression.GreaterThanOrEqual(member, constant),
            "lt"  => Expression.LessThan(member, constant),
            "lte" => Expression.LessThanOrEqual(member, constant),
            _     => null,
        };

        if (body is null) return query;
        return query.Where(Expression.Lambda<Func<T, bool>>(body, param));
    }

    // ---- Type coercion ----

    private static object? ConvertValue(string raw, Type targetType)
    {
        // Unwrap Nullable<T>
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        try
        {
            if (underlying == typeof(string))   return raw;
            if (underlying == typeof(int))      return int.Parse(raw);
            if (underlying == typeof(long))     return long.Parse(raw);
            if (underlying == typeof(decimal))  return decimal.Parse(raw);
            if (underlying == typeof(double))   return double.Parse(raw);
            if (underlying == typeof(float))    return float.Parse(raw);
            if (underlying == typeof(bool))     return raw is "1" or "true" ? true : false;
            if (underlying == typeof(DateTime)) return DateTime.Parse(raw);
            if (underlying == typeof(Guid))     return Guid.Parse(raw);
            if (underlying.IsEnum)              return Enum.Parse(underlying, raw, ignoreCase: true);
        }
        catch
        {
            return null; // unparseable — skip filter
        }

        return raw;
    }
}
