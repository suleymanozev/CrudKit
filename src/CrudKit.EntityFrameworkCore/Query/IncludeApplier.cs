using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Enums;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Applies EF Core Include() calls based on [DefaultInclude] class-level attributes or a
/// client-supplied comma-separated include parameter.
/// </summary>
public static class IncludeApplier
{
    /// <summary>
    /// Applies navigation property includes to the query.
    /// </summary>
    /// <param name="query">The base queryable.</param>
    /// <param name="includeParam">
    /// Optional client override. "none" suppresses all includes.
    /// A comma-separated list of property names overrides the defaults.
    /// </param>
    /// <param name="isDetailQuery">
    /// True when called from a detail (FindById) context.
    /// Controls whether DetailOnly-scoped attributes are applied.
    /// </param>
    public static IQueryable<T> Apply<T>(IQueryable<T> query, string? includeParam, bool isDetailQuery)
        where T : class
    {
        // "none" → skip all includes
        if (string.Equals(includeParam, "none", StringComparison.OrdinalIgnoreCase))
            return query;

        // Explicit client override: apply only the requested properties
        if (!string.IsNullOrWhiteSpace(includeParam))
        {
            var names = includeParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var validProps = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (validProps.Contains(name))
                    query = query.Include(name);
            }
            return query;
        }

        // Default: read class-level [DefaultInclude] attributes and filter by scope
        var attributes = typeof(T).GetCustomAttributes<DefaultIncludeAttribute>(inherit: true);
        foreach (var attr in attributes)
        {
            if (attr.Scope == IncludeScope.DetailOnly && !isDetailQuery)
                continue;
            query = query.Include(attr.NavigationProperty);
        }

        return query;
    }
}
