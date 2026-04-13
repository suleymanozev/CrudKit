using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CrudKit.Api.Configuration;
using CrudKit.Api.Endpoints;
using CrudKit.Api.Events;
using CrudKit.Api.Tenancy;
using CrudKit.Api.Validation;
using CrudKit.Core.Attributes;
using CrudKit.Core.Auth;
using CrudKit.Core.Events;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Tenancy;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Auditing;
using CrudKit.EntityFrameworkCore.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrudKit.Api.Extensions;

public static class CrudKitAppExtensions
{
    public static IServiceCollection AddCrudKit<TContext>(
        this IServiceCollection services,
        Action<CrudKitApiOptions>? configure = null)
        where TContext : CrudKitDbContext
    {
        services.AddCrudKitEf<TContext>();

        var opts = new CrudKitApiOptions();
        configure?.Invoke(opts);
        services.TryAddSingleton(opts);

        // Propagate relevant flags to the EF layer options
        services.AddSingleton(new CrudKitEfOptions
        {
            AuditTrailEnabled = opts.AuditTrailEnabled,
            EnumAsStringEnabled = opts.EnumAsStringEnabled,
            AuditFailedOperations = opts.AuditFailedOperations,
            AuditSchema = opts.AuditSchema,
            DomainEventsEnabled = opts.DomainEventsEnabled,
        });

        // Register the accessor that determines which DbContext audit entries are written to.
        // When UseContext<T>() is configured, audit goes to that dedicated context.
        services.AddSingleton(new AuditDbContextAccessor(opts.AuditContextType));

        // Register audit writer when audit trail is enabled
        if (opts.AuditTrailEnabled)
        {
            if (opts.CustomAuditWriterType is not null)
                services.TryAddScoped(typeof(IAuditWriter), opts.CustomAuditWriterType);
            else
                services.TryAddScoped<IAuditWriter, DbAuditWriter>();
        }

        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        // Register ITenantContext (always — even if no resolver, allows manual setting)
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        if (opts.TenantResolver is not null)
        {
            services.AddSingleton(new TenantResolverOptions
            {
                Resolver = opts.TenantResolver,
                RejectUnresolved = opts.TenantRejectUnresolved,
                Policy = opts.CrossTenantPolicyInstance
            });
        }

        services.Configure<JsonOptions>(jsonOpts =>
        {
            jsonOpts.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            jsonOpts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

            // Hide TenantId from API responses — it's a system field not useful to consumers
            jsonOpts.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    static typeInfo =>
                    {
                        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
                        for (var i = typeInfo.Properties.Count - 1; i >= 0; i--)
                        {
                            if (typeInfo.Properties[i].Name is "tenantId" or "TenantId")
                                typeInfo.Properties.RemoveAt(i);
                        }
                    }
                }
            };
        });

        services.AddHostedService<CrudKitStartupValidator>();

        // Register global hooks
        foreach (var hookType in opts.GlobalHookTypes)
            services.AddScoped(typeof(IGlobalCrudHook), hookType);

        // Register domain event dispatcher
        if (opts.DomainEventsEnabled)
        {
            if (opts.CustomDomainEventDispatcherType is not null)
                services.TryAddScoped(typeof(IDomainEventDispatcher), opts.CustomDomainEventDispatcherType);
            else
                services.TryAddScoped<IDomainEventDispatcher, CrudKitEventDispatcher>();

            // Assembly-scan for handlers
            foreach (var assembly in opts.DomainEventHandlerAssemblies)
            {
                var handlerTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface)
                    .SelectMany(t => t.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>))
                        .Select(i => new { Interface = i, Implementation = t }));

                foreach (var pair in handlerTypes)
                    services.AddScoped(pair.Interface, pair.Implementation);
            }
        }

        if (opts.ScanModulesFromAssembly is not null)
        {
            var moduleTypes = opts.ScanModulesFromAssembly.GetTypes()
                .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);
            foreach (var moduleType in moduleTypes)
                services.AddSingleton(typeof(IModule), moduleType);
        }

        return services;
    }

    public static WebApplication UseCrudKit(this WebApplication app)
    {
        // Add tenant resolver middleware if configured
        var resolverOptions = app.Services.GetService<TenantResolverOptions>();
        if (resolverOptions is not null)
        {
            app.UseMiddleware<TenantResolverMiddleware>();
        }

        // Module endpoints (manual registration via IModule)
        foreach (var module in app.Services.GetServices<IModule>())
            module.MapEndpoints(app);

        // Auto-scan: find all [CrudEntity] entities and register endpoints
        AutoRegisterCrudEndpoints(app);

        return app;
    }

    /// <summary>
    /// Scans all loaded assemblies for types decorated with [CrudEntity] and registers
    /// CRUD endpoints automatically. Skips entities already registered by modules or
    /// manual MapCrudEndpoints calls. When [CreateDtoFor] / [UpdateDtoFor] DTOs exist
    /// in the entity's assembly, they are wired up automatically.
    /// </summary>
    private static void AutoRegisterCrudEndpoints(WebApplication app)
    {
        var crudEntityTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => !t.IsAbstract && t.IsClass
                && t.GetCustomAttribute<CrudEntityAttribute>() is not null
                && typeof(IEntity).IsAssignableFrom(t))
            .ToList();

        foreach (var entityType in crudEntityTypes)
        {
            // Skip if already registered by a module or manual MapCrudEndpoints call
            if (CrudEndpointMapper.IsRegistered(app, entityType))
                continue;

            // Scan the entity's assembly for [CreateDtoFor] and [UpdateDtoFor] DTOs
            Type? createDtoType = null;
            Type? updateDtoType = null;

            foreach (var type in entityType.Assembly.GetTypes())
            {
                var createAttr = type.GetCustomAttribute<CreateDtoForAttribute>();
                if (createAttr?.EntityType == entityType)
                    createDtoType = type;

                var updateAttr = type.GetCustomAttribute<UpdateDtoForAttribute>();
                if (updateAttr?.EntityType == entityType)
                    updateDtoType = type;
            }

            var route = CrudEndpointMapper.ResolveRouteForType(entityType);

            if (createDtoType is not null && updateDtoType is not null)
            {
                // MapCrudEndpoints<TEntity, TCreate, TUpdate>(app, route)
                var method = typeof(CrudEndpointMapper)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "MapCrudEndpoints"
                        && m.GetGenericArguments().Length == 3
                        && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType == typeof(string))
                    .MakeGenericMethod(entityType, createDtoType, updateDtoType);
                var group = method.Invoke(null, [app, route]);

                var applyMethod = typeof(CrudEndpointMapper)
                    .GetMethod("ApplyEndpointConfigurer")!
                    .MakeGenericMethod(entityType);
                applyMethod.Invoke(null, [group]);
            }
            else
            {
                // MapCrudEndpoints<TEntity>(app, route) — entity-as-DTO
                var method = typeof(CrudEndpointMapper)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "MapCrudEndpoints"
                        && m.GetGenericArguments().Length == 1
                        && m.GetParameters().Length == 2
                        && m.GetParameters()[1].ParameterType == typeof(string))
                    .MakeGenericMethod(entityType);
                var group = method.Invoke(null, [app, route]);

                var applyMethod = typeof(CrudEndpointMapper)
                    .GetMethod("ApplyEndpointConfigurer")!
                    .MakeGenericMethod(entityType);
                applyMethod.Invoke(null, [group]);
            }
        }
    }

    public static IServiceCollection AddCrudKitModule<TModule>(this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        services.AddSingleton<IModule, TModule>();
        return services;
    }
}
