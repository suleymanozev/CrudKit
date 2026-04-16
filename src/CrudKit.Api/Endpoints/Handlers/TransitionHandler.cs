using System.Reflection;
using CrudKit.Core.Interfaces;
using CrudKit.Core.Models;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrudKit.Api.Endpoints.Handlers;

/// <summary>Maps POST /{id}/transition/{action} endpoint for IStateMachine entities.</summary>
internal static class TransitionHandler
{
    /// <summary>
    /// Uses reflection to check if TEntity implements IStateMachine and maps the transition endpoint.
    /// </summary>
    public static void MapIfStateMachine<TEntity>(RouteGroupBuilder group, string route, string tag)
        where TEntity : class, IEntity
    {
        // Find IStateMachine<TState> on TEntity
        var smInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachine<>));
        if (smInterface is null) return;

        var stateType = smInterface.GetGenericArguments()[0];

        // Read static Transitions property
        var transitionsProp = typeof(TEntity).GetProperty("Transitions",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (transitionsProp is null) return;

        // Status property on the entity
        var statusProp = typeof(TEntity).GetProperty("Status",
            BindingFlags.Public | BindingFlags.Instance);
        if (statusProp is null) return;

        // Check if entity implements IStateMachineWithPayload<TState>
        var payloadInterface = typeof(TEntity).GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStateMachineWithPayload<>));

        IReadOnlyDictionary<string, Type>? payloadMap = null;
        if (payloadInterface is not null)
        {
            var payloadsProp = typeof(TEntity).GetProperty("TransitionPayloads",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            var rawMap = payloadsProp?.GetValue(null) as IReadOnlyDictionary<string, Type>;
            if (rawMap is not null)
                payloadMap = new Dictionary<string, Type>(rawMap, StringComparer.OrdinalIgnoreCase);
        }

        group.MapPost("/{id}/transition/{action}", async (string id, string action, HttpContext httpCtx, IRepo<TEntity> repo, CancellationToken ct) =>
        {
            var guid = CrudEndpointMapper.ParseGuid(id, typeof(TEntity).Name);

            // Load entity to validate current state (untracked — SetProperty will reload tracked)
            var entity = await repo.FindByIdOrDefault(guid, ct)
                ?? throw AppError.NotFound($"{typeof(TEntity).Name} with id '{id}' was not found.");

            var currentStatus = statusProp.GetValue(entity)!;
            var transitions = transitionsProp.GetValue(null)!;

            // Check if the requested action is valid from the current state
            var found = false;
            object? targetStatus = null;

            foreach (var t in (System.Collections.IEnumerable)transitions)
            {
                var tType = t.GetType();
                var fromField = tType.GetField("Item1");
                var toField = tType.GetField("Item2");
                var actField = tType.GetField("Item3");
                if (fromField is null || toField is null || actField is null)
                    throw new InvalidOperationException($"Invalid Transitions format on {typeof(TEntity).Name}. Expected (TState From, TState To, string Action) tuples.");
                var from = fromField.GetValue(t)!;
                var to = toField.GetValue(t)!;
                var act = (string)actField.GetValue(t)!;

                if (from.Equals(currentStatus) && string.Equals(act, action, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    targetStatus = to;
                    break;
                }
            }

            if (!found)
                throw AppError.BadRequest($"Invalid transition '{action}' from state '{currentStatus}'.");

            // Deserialize typed payload if required
            object? payload = null;
            if (payloadMap is not null && payloadMap.TryGetValue(action, out var payloadType))
            {
                if (httpCtx.Request.ContentLength is null or 0 && !httpCtx.Request.HasJsonContentType())
                {
                    throw AppError.BadRequest($"Transition '{action}' requires a payload of type {payloadType.Name}.");
                }
                try
                {
                    payload = await httpCtx.Request.ReadFromJsonAsync(payloadType, ct);
                }
                catch (Exception) when (payload is null)
                {
                    throw AppError.BadRequest($"Invalid payload for transition '{action}'. Expected {payloadType.Name}.");
                }
                if (payload is null)
                    throw AppError.BadRequest($"Transition '{action}' requires a payload of type {payloadType.Name}.");
            }

            // Resolve transition hook
            var hook = httpCtx.RequestServices.GetService<ITransitionHook<TEntity>>();
            var appCtx = CrudEndpointMapper.BuildAppContext(httpCtx);

            if (hook is not null)
                await hook.BeforeTransition(entity, action, payload, appCtx);

            // Set status via repo — no direct DbContext access
            await using var tx = await repo.BeginTransactionAsync(ct);
            var updated = await repo.SetProperty(guid, "Status", targetStatus, ct);
            await tx.CommitAsync(ct);

            if (hook is not null)
                await hook.AfterTransition(updated, action, payload, appCtx);

            return Results.Ok(updated);
        })
        .WithName($"Transition{tag}")
        .Produces<TEntity>(200)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(500);
    }
}
