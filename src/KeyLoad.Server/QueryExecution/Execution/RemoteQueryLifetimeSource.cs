using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.QueryExecution;

internal static class RemoteQueryLifetimeSource
{
    internal static PartitionQuerySourceFenceV2 Capture(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, ResourceDefinition resource, string digest, PartitionRef partition,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
    {
        var charged = budget.CreateView(view, grant);
        var coverage = PartitionControlLifetimeCoverage.Capture(charged, partition,
            [resource], database.Limits.MaxBatchBytes);
        budget.CaptureAuthenticatedResourceLifetimes(database, charged, coverage, [resource]);
        var original = new PartitionQuerySourceFence(database.CaptureRemotePartitionQuery(view, principal,
            partition, resource.Name, grant), digest);
        return new(original, new(resource, coverage, principal));
    }

    internal static void RequireSame(PartitionQuerySourceFenceV2 expected, PartitionQuerySourceFenceV2 current,
        string detail)
    {
        if (!JsonDefaults.Serialize(expected).AsSpan().SequenceEqual(JsonDefaults.Serialize(current)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, detail);
        }
    }
}
