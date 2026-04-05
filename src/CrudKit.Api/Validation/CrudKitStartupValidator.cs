using System.Reflection;
using CrudKit.Core.Attributes;
using CrudKit.Core.Interfaces;
using CrudKit.EntityFrameworkCore;
using CrudKit.EntityFrameworkCore.Concurrency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CrudKit.Api.Validation;

/// <summary>
/// Validates entity metadata at application startup.
/// Checks CrudEntity attribute configuration against actual entity properties.
/// </summary>
public class CrudKitStartupValidator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<CrudKitStartupValidator> _logger;

    public CrudKitStartupValidator(IServiceProvider services, ILogger<CrudKitStartupValidator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Validate();
        _logger.LogDebug("CrudKitStartupValidator: startup validation complete.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Runs all entity metadata validations. Called on startup and exposed for testing.
    /// </summary>
    public void Validate()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetService<CrudKitDbContext>();
        if (db is null) return;

        var entityTypes = db.Model.GetEntityTypes()
            .Where(t => t.ClrType != null && typeof(IAuditableEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var attr = entityType.GetCustomAttribute<CrudEntityAttribute>();
            if (attr is null) continue;

            ValidateOwnerField(entityType, attr);
            ValidateWorkflowProtected(entityType, attr);
            ValidateConcurrentBulkUpdate(entityType, attr);
        }
    }

    /// <summary>
    /// Checks that the OwnerField property actually exists on the entity.
    /// </summary>
    private void ValidateOwnerField(Type entityType, CrudEntityAttribute attr)
    {
        if (string.IsNullOrEmpty(attr.OwnerField)) return;

        var prop = entityType.GetProperty(attr.OwnerField,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (prop is null)
        {
            throw new InvalidOperationException(
                $"[CrudEntity] on '{entityType.Name}' specifies OwnerField='{attr.OwnerField}', " +
                $"but no such property exists on the entity.");
        }
    }

    /// <summary>
    /// Checks that all WorkflowProtected field names reference existing properties.
    /// </summary>
    private void ValidateWorkflowProtected(Type entityType, CrudEntityAttribute attr)
    {
        if (attr.WorkflowProtected is null || attr.WorkflowProtected.Length == 0) return;

        foreach (var field in attr.WorkflowProtected)
        {
            var prop = entityType.GetProperty(field,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

            if (prop is null)
            {
                throw new InvalidOperationException(
                    $"[CrudEntity] on '{entityType.Name}' specifies WorkflowProtected field '{field}', " +
                    $"but no such property exists on the entity.");
            }
        }
    }

    /// <summary>
    /// Logs a warning (does not throw) when an entity implements IConcurrent and has EnableBulkUpdate.
    /// Bulk updates bypass optimistic concurrency checks.
    /// </summary>
    private void ValidateConcurrentBulkUpdate(Type entityType, CrudEntityAttribute attr)
    {
        if (!attr.EnableBulkUpdate) return;
        if (!typeof(IConcurrent).IsAssignableFrom(entityType)) return;

        _logger.LogWarning(
            "[CrudEntity] on '{EntityType}' enables bulk update on a concurrent entity. " +
            "Bulk updates bypass optimistic concurrency checks (RowVersion).",
            entityType.Name);
    }
}
