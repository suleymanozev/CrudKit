using System.Text.Json.Serialization;
using CrudKit.Api.Configuration;
using CrudKit.Api.Tenancy;
using CrudKit.Api.Validation;
using CrudKit.Core.Auth;
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
        });

        // Register audit writer when audit trail is enabled
        if (opts.AuditTrailEnabled)
        {
            if (opts.CustomAuditWriterType != null)
                services.TryAddScoped(typeof(IAuditWriter), opts.CustomAuditWriterType);
            else
                services.TryAddScoped<IAuditWriter, DbAuditWriter>();
        }

        services.TryAddScoped<ICurrentUser, AnonymousCurrentUser>();

        // Register ITenantContext (always — even if no resolver, allows manual setting)
        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        if (opts.TenantResolver != null)
        {
            services.AddSingleton(new TenantResolverOptions { Resolver = opts.TenantResolver });
        }

        services.Configure<JsonOptions>(jsonOpts =>
        {
            jsonOpts.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            jsonOpts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        services.AddHostedService<CrudKitStartupValidator>();

        // Register global hooks
        foreach (var hookType in opts.GlobalHookTypes)
            services.AddScoped(typeof(IGlobalCrudHook), hookType);

        if (opts.ScanModulesFromAssembly != null)
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
        if (resolverOptions != null)
        {
            app.UseMiddleware<TenantResolverMiddleware>();
        }

        foreach (var module in app.Services.GetServices<IModule>())
            module.MapEndpoints(app);
        return app;
    }

    public static IServiceCollection AddCrudKitModule<TModule>(this IServiceCollection services)
        where TModule : class, IModule, new()
    {
        services.AddSingleton<IModule, TModule>();
        return services;
    }
}
