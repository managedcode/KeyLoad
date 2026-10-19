using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Query.Features.QueryExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

/// <summary>Prepares one original request's bounded leaves for their actual execution storage owners.</summary>
internal sealed class PartitionQueryLeafDispatch(DatabaseEngine database, string principalId, IServiceProvider services,
    GrainRequestCodec codec, DecodedGrainRequest original)
{
    private readonly IGrainFactory grains = services.GetRequiredService<IGrainFactory>();

    internal async Task<PartitionQueryPreparedLeaf> PrepareAsync(PartitionQueryLeafPlan plan,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget parent, CancellationToken token)
    {
        parent.Check();
        var source = CaptureMetadata(plan, grant, parent);
        var target = grains.GetGrain<IPartitionQueryGrain>(PartitionQueryGrainIdentity.For(plan.Partition));
        var selected = await target.ResolveOwnerAsync(database.Store.Identity.Incarnation, token).ConfigureAwait(true);
        parent.Check();
        if (selected.Address is null || selected.Owner is null || selected.Owner.PhysicalShardId == Guid.Empty
            || selected.Owner.Incarnation != database.Store.Identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var downward = plan with
        {
            MaxReadBytes = grant.RemainingBytes,
            MaxExaminedRecords = grant.RemainingRecords
        };
        var metadata = new PartitionQueryOwnedLeafRequest(downward, selected.Owner, plan.Partition.TenantId,
            source.Definition, source.Coverage, source.Principal);
        parent.ChargeBytes(OrleansSerialization.Measure(metadata));
        var childId = Guid.NewGuid();
        var signed = codec.CreatePartitionQueryLeaf(childId, principalId,
            OrleansSerialization.Serialize(metadata), parent.ExpiresAt);
        parent.ChargeBytes(checked((long)sizeof(char) * signed.Length));
        return new(childToken => RunAsync(target, selected.Address, selected.Owner, signed, childId, childToken),
            (result, childToken) => Complete(parent, grant, result, childToken));
    }

    private PartitionQueryLifetimeMetadata CaptureMetadata(PartitionQueryLeafPlan plan,
        ReadExecutionBudgetReadGrant grant, ReadExecutionBudget parent)
    {
        using var scope = parent.EnterReadGrant(grant);
        return database.WithQueryView(principalId, plan.Partition, plan.Request.Query.Collection,
            Capability.None, (view, principal, definition) => new PartitionQueryLifetimeMetadata(definition,
                parent.BindResourceLifetimes(database, view, plan.Partition, [definition]), principal), parent);
    }

    private async Task<PartitionQueryOwnedLeaf> RunAsync(IPartitionQueryGrain target, SiloAddress address,
        PhysicalShardRecord owner, string signed, Guid childId, CancellationToken token)
    {
        using var identity = GrainRequestIdentityScope.ForChild(services, original.Envelope,
            childId, Guid.Empty, token);
        var reply = await target.ExecuteAsync(address, signed, token).ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? GrainRoutingProtocol.InvalidRequest);
        }
        var result = GrainPayload.Read<GrainValue>(reply.Payload).Value as PartitionQueryLeafResult
            ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        return new(owner, result);
    }

    private static Task Complete(ReadExecutionBudget parent, ReadExecutionBudgetReadGrant grant,
        PartitionQueryOwnedLeaf result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        parent.ImportReadGrant(grant, result.Result.ReadBytes, result.Result.ExaminedRecords);
        parent.Check();
        return Task.CompletedTask;
    }
}
