using KeyLoad.Clustering;
using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentDeadline
{
    internal static DateTimeOffset Expiry(ReadExecutionBudget work, TimeProvider clock, IOptions<GrainRoutingOptions> routing)
    {
        var now = clock.GetUtcNow();
        var remaining = work.RemainingLifetime;
        var lifetime = remaining < routing.Value.RequestLifetime ? remaining : routing.Value.RequestLifetime;
        return now + lifetime;
    }
}
