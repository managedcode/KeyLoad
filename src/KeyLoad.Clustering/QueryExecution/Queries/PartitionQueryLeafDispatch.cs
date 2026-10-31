using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution.Execution;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Clustering;

/// <summary>Prepares one original request's bounded leaves for their actual execution storage owners.</summary>
internal sealed class PartitionQueryLeafDispatch(DatabaseEngine database, string principalId, IServiceProvider services,
    GrainRequestCodec codec, DecodedGrainRequest original)
{
    private readonly PartitionLeafCalls leaves = new(database, principalId, services, original);

    internal async Task<PartitionQueryPreparedLeaf> PrepareAsync(PartitionQueryLeafPlan plan,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget parent, CancellationToken token)
    {
        parent.Check();
        PartitionQueryLifetimeMetadata source;
        using (parent.EnterReadGrant(grant))
        {
            source = leaves.CaptureMetadata(plan.Partition, plan.Request.Query.Collection, parent);
        }
        var target = leaves.Target(plan.Partition);
        var selected = await leaves.ResolveOwnerAsync(target, parent, token).ConfigureAwait(true);
        var downward = plan with
        {
            MaxReadBytes = grant.RemainingBytes,
            MaxExaminedRecords = grant.RemainingRecords
        };
        var metadata = new PartitionQueryOwnedLeafRequest(downward, selected.Owner, plan.Partition.TenantId,
            source.Definition, source.Coverage, source.Principal);
        parent.ChargeBytes(OrleansSerialization.Measure(metadata));
        var childId = Guid.NewGuid();
        var child = codec.CreatePartitionQueryLeaf(childId, principalId, metadata, parent.ExpiresAt);
        parent.ChargeBytes(OrleansSerialization.Measure(child));
        return new(async childToken => new PartitionQueryOwnedLeaf(selected.Owner,
                await leaves.RunAsync<PartitionQueryLeafResult>(target, selected.Address, child, childId, childToken).ConfigureAwait(true)),
            (result, childToken) => PartitionLeafCalls.CompleteAsync(parent, grant, result.Result.ReadBytes,
                result.Result.ExaminedRecords, childToken));
    }
}
