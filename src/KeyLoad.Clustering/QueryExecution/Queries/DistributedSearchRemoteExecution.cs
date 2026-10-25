using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution.Execution;
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
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();
    private readonly QueryExecutionOptions options = services.GetRequiredService<IOptions<QueryExecutionOptions>>().Value;
    private readonly Dictionary<PartitionRef, (SiloAddress Address, PhysicalShardRecord Owner)> owners = [];

    internal async Task<DistributedSearchPreparedLeaf> PrepareAsync(DistributedSearchPhaseWork phase,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget parent, CancellationToken token)
    {
        parent.Check();
        var source = database.WithQueryView(principalId, phase.Search.Partition, phase.Search.Collection,
            Capability.None, (view, principal, definition) => (Definition: definition,
                Coverage: parent.BindResourceLifetimes(view, phase.Search.Partition, [definition]),
                Principal: principal), parent);
        var target = grains.GetGrain<IPartitionQueryGrain>(PartitionQueryGrainIdentity.For(phase.Search.Partition));
        var selected = await ResolveOwnerAsync(target, phase, parent, token).ConfigureAwait(true);
        var request = new DistributedSearchOwnedLeaf(ContractVersion, phase.Phase, phase.Search,
            selected.Owner, phase.Search.Partition.TenantId, grant.RemainingBytes, grant.RemainingRecords,
            phase.MaxResultBytes, phase.Witness, phase.Statistics, phase.Scope, phase.SourceWindowId, phase.Selected, source.Definition, source.Coverage, source.Principal);
        parent.ChargeBytes(OrleansSerialization.Measure(request));
        var childId = Guid.NewGuid();
        var child = codec.CreateDistributedSearchLeaf(childId, principalId,
            OrleansSerialization.Serialize(request), parent.ExpiresAt);
        parent.ChargeBytes(OrleansSerialization.Measure(child));
        return new(request, childToken => RunAsync(target, selected.Address, child, childId, childToken),
            (result, childToken) => Complete(parent, grant, result, childToken));
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
            parent.ChargeBytes(PartitionQueryRetention.LeafDescriptorBytes);
            var selected = await target.ResolveOwnerAsync(database.Store.Identity.Incarnation, token).ConfigureAwait(true);
            parent.Check();
            if (selected.Address is null || selected.Owner is null
                || selected.Owner.PhysicalShardId == Guid.Empty
                || selected.Owner.Incarnation != database.Store.Identity.Incarnation)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
            }
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

    private async Task<DistributedSearchLeafResult> RunAsync(IPartitionQueryGrain target, SiloAddress owner,
        GrainRequestEnvelope child, Guid childId, CancellationToken token)
    {
        using var identity = GrainRequestIdentityScope.ForChild(services, original.Envelope,
            childId, Guid.Empty, token);
        var reply = await target.ExecuteAsync(owner, child, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        return GrainPayload.Read<GrainValue>(reply.Payload).Value as DistributedSearchLeafResult
            ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
    }

    private static Task Complete(ReadExecutionBudget parent, ReadExecutionBudgetReadGrant grant,
        DistributedSearchLeafResult result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        parent.ImportReadGrant(grant, result.ReadBytes, result.ExaminedRecords);
        parent.Check();
        return Task.CompletedTask;
    }
}
