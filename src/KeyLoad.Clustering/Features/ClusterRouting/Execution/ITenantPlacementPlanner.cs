using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal interface ITenantPlacementPlanner
{
    Task<TenantPlacementPlanResultV1> PlanAsync(GrainRequestEnvelope trusted, TenantPlacementPlanRequest request,
        ReadExecutionBudget work, CancellationToken token);
}
