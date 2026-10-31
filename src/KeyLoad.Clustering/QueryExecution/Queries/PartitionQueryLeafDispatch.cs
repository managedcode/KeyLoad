using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Clustering;

/// <summary>Prepares one original request's bounded leaves for their actual execution storage owners.</summary>
internal sealed class PartitionQueryLeafDispatch(DatabaseEngine database, string principalId, IServiceProvider services,
    GrainRequestCodec codec, DecodedGrainRequest original)
{
    private readonly PartitionLeafCalls leaves = new(database, principalId, services, original);

    internal async Task<PartitionQueryPreparedLeaf> PrepareAsync(PartitionQueryLeafPlan plan, ReadExecutionBudget parent, CancellationToken token)
    {
        parent.Check();
        var source = leaves.CaptureMetadata(plan.Partition, plan.Request.Query.Collection, parent);
        var target = leaves.Target(plan.Partition);
        var selected = await leaves.ResolveOwnerAsync(target, parent, token).ConfigureAwait(true);
        var metadata = new PartitionQueryOwnedLeafRequest(plan, selected.Owner, plan.Partition.TenantId,
            source.Definition, source.Coverage, source.Principal);
        var childId = Guid.NewGuid();
        var child = codec.CreatePartitionQueryLeaf(childId, principalId, metadata);
        return new(async childToken => new PartitionQueryOwnedLeaf(selected.Owner,
                await leaves.RunAsync<PartitionQueryLeafResult>(target, selected.Address, child, childId, childToken).ConfigureAwait(true)),
            (_, childToken) => PartitionLeafCalls.CompleteAsync(parent, childToken));
    }
}
