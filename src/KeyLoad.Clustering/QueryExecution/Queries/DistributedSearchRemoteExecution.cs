using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Query.QueryExecution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Retains one actual storage address per unique partition for all phases of one original request.</summary>
internal sealed class DistributedSearchRemoteExecution(DatabaseEngine database, string principalId, IServiceProvider services,
    GrainRequestCodec codec, DecodedGrainRequest original)
{
    private const int ContractVersion = 1;
    private readonly PartitionLeafCalls leaves = new(database, principalId, services, original);
    private readonly QueryExecutionOptions options = services.GetRequiredService<IOptions<QueryExecutionOptions>>().Value;
    private readonly Dictionary<PartitionRef, (SiloAddress Address, PhysicalShardRecord Owner)> owners = [];

    internal async Task<DistributedSearchPreparedLeaf> PrepareAsync(DistributedSearchPhaseWork phase, ReadExecutionBudget parent, CancellationToken token)
    {
        parent.Check();
        var source = leaves.CaptureMetadata(phase.Search.Partition, phase.Search.Collection, parent);
        var target = leaves.Target(phase.Search.Partition);
        var selected = await ResolveOwnerAsync(target, phase, parent, token).ConfigureAwait(true);
        var request = new DistributedSearchOwnedLeaf(ContractVersion, phase.Phase, phase.Search,
            selected.Owner, phase.Search.Partition.TenantId, phase.Witness, phase.Statistics, phase.Scope, phase.SourceWindowId, phase.Selected, source.Definition, source.Coverage, source.Principal);
        var childId = Guid.NewGuid();
        var child = codec.CreateDistributedSearchLeaf(childId, principalId, request, parent.ExpiresAt);
        return new(request, childToken => leaves.RunAsync<DistributedSearchLeafResult>(target, selected.Address, child,
                childId, childToken),
            (_, childToken) => PartitionLeafCalls.CompleteAsync(parent, childToken));
    }

    private async Task<(SiloAddress Address, PhysicalShardRecord Owner)> ResolveOwnerAsync(IPartitionQueryGrain target,
        DistributedSearchPhaseWork phase, ReadExecutionBudget parent, CancellationToken token)
    {
        parent.Check();
        if (phase.Phase == DistributedSearchPhase.Statistics)
        {
            if (owners.Count >= options.MaximumPartitions || owners.ContainsKey(phase.Search.Partition))
            {
                throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
            }
            var selected = await leaves.ResolveOwnerAsync(target, parent, token).ConfigureAwait(true);
            owners.Add(phase.Search.Partition, selected);
            return selected;
        }
        if (!owners.TryGetValue(phase.Search.Partition, out var retained)
            || phase.Witness is null || phase.Witness.Owner != retained.Owner)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        return retained;
    }
}
