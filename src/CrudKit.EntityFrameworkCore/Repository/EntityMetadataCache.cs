using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

namespace CrudKit.EntityFrameworkCore.Repository;

/// <summary>
/// Caches reflection metadata for entity and DTO types.
/// All results are immutable after first computation — safe for concurrent access.
/// </summary>
internal static class EntityMetadataCache
{
    // Cache per entity type
    private static readonly ConcurrentDictionary<Type, EntityTypeInfo> EntityCache = new();

    // Cache for SetProperty MethodInfo per UpdateSettersBuilder<T> type
    private static readonly ConcurrentDictionary<Type, MethodInfo?> SetPropertyMethodCache = new();

    // Cache per DTO type
    private static readonly ConcurrentDictionary<Type, DtoTypeInfo> DtoCache = new();

    // Cache per (entityType, dtoType) pair for property mapping
    private static readonly ConcurrentDictionary<(Type Entity, Type Dto), PropertyMapping[]> MappingCache = new();

    public static EntityTypeInfo GetEntityInfo(Type entityType)
    {
        return EntityCache.GetOrAdd(entityType, t =>
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propMap = props.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            var uniqueProps = props
                .Where(p => p.GetCustomAttribute<UniqueAttribute>() is not null)
                .ToArray();

            var skipResponseProps = props
                .Where(p => p.GetCustomAttribute<SkipResponseAttribute>() is not null && p.CanWrite)
                .Where(p => !p.PropertyType.IsValueType || Nullable.GetUnderlyingType(p.PropertyType) is not null)
                .ToArray();

            var cascadeAttributes = t.GetCustomAttributes<CascadeSoftDeleteAttribute>().ToArray();

            var flattenProps = props
                .Where(p => p.GetCustomAttribute<FlattenAttribute>() is not null
                    && p.PropertyType.GetCustomAttribute<ValueObjectAttribute>() is not null)
                .ToArray();

            return new EntityTypeInfo(props, propMap, uniqueProps, skipResponseProps, cascadeAttributes, flattenProps);
        });
    }

    public static DtoTypeInfo GetDtoInfo(Type dtoType)
    {
        return DtoCache.GetOrAdd(dtoType, t =>
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            return new DtoTypeInfo(props);
        });
    }

    public static PropertyMapping[] GetMappings(Type entityType, Type dtoType)
    {
        var key = (entityType, dtoType);
        return MappingCache.GetOrAdd(key, k =>
        {
            var entityInfo = GetEntityInfo(k.Entity);
            var dtoInfo = GetDtoInfo(k.Dto);

            var mappings = new List<PropertyMapping>();
            foreach (var dtoProp in dtoInfo.Properties)
            {
                if (!entityInfo.PropertyMap.TryGetValue(dtoProp.Name, out var entityProp)) continue;

                // Skip system fields
                if (entityProp.Name is "Id" or "CreatedAt" or "UpdatedAt"
                    or "DeletedAt" or "DeleteBatchId" or "TenantId") continue;

                // Skip read-only
                if (!entityProp.CanWrite) continue;

                var isProtected = entityProp.GetCustomAttribute<ProtectedAttribute>() is not null;
                var isSkipUpdate = entityProp.GetCustomAttribute<SkipUpdateAttribute>() is not null;
                var isHashed = entityProp.GetCustomAttribute<HashedAttribute>() is not null;
                var isOptional = IsOptionalType(dtoProp.PropertyType);

                mappings.Add(new PropertyMapping(
                    dtoProp, entityProp, isProtected, isSkipUpdate, isHashed, isOptional));
            }
            return mappings.ToArray();
        });
    }

    /// <summary>
    /// Returns the non-expression SetProperty overload on UpdateSettersBuilder&lt;T&gt;,
    /// caching the result so BulkUpdate does not reflect on every call.
    /// </summary>
    public static MethodInfo? GetSetPropertyMethod(Type builderType)
    {
        return SetPropertyMethodCache.GetOrAdd(builderType, t =>
        {
            return t.GetMethods()
                .FirstOrDefault(m => m.Name == "SetProperty"
                    && m.IsGenericMethodDefinition
                    && m.GetGenericArguments().Length == 1
                    && m.GetParameters().Length == 2
                    && !(m.GetParameters()[1].ParameterType.IsGenericType
                        && m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>)));
        });
    }

    private static bool IsOptionalType(Type type)
        => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Optional<>);
}

internal sealed record EntityTypeInfo(
    PropertyInfo[] Properties,
    Dictionary<string, PropertyInfo> PropertyMap,
    PropertyInfo[] UniqueProperties,
    PropertyInfo[] SkipResponseProperties,
    CascadeSoftDeleteAttribute[] CascadeAttributes,
    PropertyInfo[] FlattenProperties);

internal sealed record DtoTypeInfo(PropertyInfo[] Properties);

internal sealed record PropertyMapping(
    PropertyInfo DtoProperty,
    PropertyInfo EntityProperty,
    bool IsProtected,
    bool IsSkipUpdate,
    bool IsHashed,
    bool IsOptional);
