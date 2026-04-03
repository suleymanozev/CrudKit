using System.Reflection;
using CrudKit.Core.Attributes;
using Microsoft.EntityFrameworkCore;

namespace CrudKit.EntityFrameworkCore.Query;

/// <summary>
/// Reads [DefaultInclude] attributes on navigation properties and applies EF Core Include().
/// </summary>
public static class IncludeApplier
{
    public static IQueryable<T> Apply<T>(IQueryable<T> query) where T : class
    {
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<DefaultIncludeAttribute>() != null)
                query = query.Include(prop.Name);
        }
        return query;
    }
}
