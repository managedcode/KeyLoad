using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class TenantPlacementReadExecution
{
    internal static async Task<object> ExecuteAsync(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest original, CancellationToken token)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        var work = new ReadExecutionBudget(services.GetRequiredService<IOptions<DatabaseLimits>>(), clock, token);
        work.ConstrainLifetime(original.Envelope.ExpiresAt);
        object result;
        switch (original.Envelope.ReadKind)
        {
            case GrainReadKind.PlacementPolicy:
                result = database.ReadPlacementPolicy(principal.Id,
                    GrainNativePayload.ReadPublicInput<TenantPlacementScope>(original.Payload), work);
                break;
            case GrainReadKind.PlacementIntent:
                var request = GrainNativePayload.ReadPublicInput<PlacementIntentReadRequest>(original.Payload);
                var intent = database.ReadPlacementIntent(principal.Id, request.Scope.TenantId,
                    request.Scope.DatabaseId, request.IntentId, work);
                if (request.Scope.ControlPartition != intent.Plan.ControlPartition)
                { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
                result = intent;
                break;
            case GrainReadKind.PlacementPlan:
                var planner = services.GetRequiredService<ITenantPlacementPlanner>();
                result = await planner.PlanAsync(original.Envelope,
                    GrainNativePayload.ReadPublicInput<TenantPlacementPlanRequest>(original.Payload), work, token).ConfigureAwait(true);
                break;
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        }
        work.CheckResult(result);
        return result;
    }
}
