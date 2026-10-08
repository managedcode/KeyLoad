using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.QueryExecution;

internal sealed class RemotePartitionQueryLeafRead(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, RemoteDocumentClient client, QueryEngine queries)
{
    private NodeOptions Options => nodeOptions.Value;
    private const int EmptyGrant = 0;
    private const string NonceFormat = "N";
    private const int MetadataPassesRemaining = 2;
    private const string FenceChanged = "The remote partition query authority changed.";
    private readonly List<(PartitionQueryLeafPlanV1 Plan, PartitionQuerySourceFence Fence,
        ReadExecutionBudgetReadGrant Grant)> original = [];

    internal PartitionQueryPreparedLeaf Prepare(GrainRequestEnvelope envelope, string principalId,
        PartitionQueryLeafPlanV1 plan, ReadExecutionBudgetReadGrant grant, ReadExecutionBudget budget)
    {
        budget.Check();
        var beforeBytes = grant.ReadBytes;
        var beforeRecords = grant.ExaminedRecords;
        var captured = Capture(principalId, plan, grant);
        var fence = captured.Authority;
        original.Add((plan, captured, grant));
        var metadataBytes = grant.ReadBytes - beforeBytes;
        var metadataRecords = grant.ExaminedRecords - beforeRecords;
        var child = plan with
        {
            MaxReadBytes = grant.RemainingBytes - checked(metadataBytes * MetadataPassesRemaining),
            MaxExaminedRecords = grant.RemainingRecords - checked(metadataRecords * MetadataPassesRemaining)
        };
        if (child.MaxReadBytes < EmptyGrant || child.MaxExaminedRecords < EmptyGrant)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, FenceChanged); }
        var local = PhysicalOwnerConfiguredTuples.Local(Options, partition);
        var request = new PartitionQueryOwnedLeafRequest(child, fence.Destination.Owner, fence.Tenant);
        var call = PrepareCall(envelope, request, fence, local);
        var wireBytes = call is null ? NativeSerialization.Measure(request) : NativeSerialization.Measure(call);
        child = PartitionQueryParallelRetention.Child(child, wireBytes);
        request = request with { Plan = child };
        call = call is null ? null : call with { QueryLeaf = request };
        if ((call is null ? NativeSerialization.Measure(request) : NativeSerialization.Measure(call)) > wireBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, FenceChanged); }
        return new(actualToken => RunAsync(envelope.ExpiresAt, principalId, request, call, actualToken),
            (actual, actualToken) =>
            {
                actualToken.ThrowIfCancellationRequested();
                budget.Check();
                PartitionQueryMerge.ValidateLeaf(child, actual.Result, plan.Partition,
                    partition.Database.Store.Identity, budget, fence.Destination.Owner);
                budget.ImportReadGrant(grant, actual.Result.ReadBytes, actual.Result.ExaminedRecords);
                RequireSame(captured, Capture(principalId, plan, grant));
                return Task.CompletedTask;
            });
    }

    private RemoteDocumentCallV1? PrepareCall(GrainRequestEnvelope envelope,
        PartitionQueryOwnedLeafRequest request, RemoteDocumentReadFenceV1 fence, RegisteredPhysicalOwnerV1 local)
    {
        if (fence.Destination.Owner.PhysicalShardId == local.Owner.PhysicalShardId)
        { return null; }
        if (!KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.Same(fence.Destination,
            PhysicalOwnerConfiguredTuples.Destination(Options, partition)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, FenceChanged); }
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, FenceChanged);
        return new(RemoteDocumentProtocol.Version, Guid.NewGuid(), Guid.NewGuid().ToString(NonceFormat),
            envelope.ExpiresAt, partition.Configuration.LocalId, discovery.SiloAddress, local.Owner, fence, null, request);
    }

    private async Task<PartitionQueryOwnedLeaf> RunAsync(DateTimeOffset expiresAt, string principalId,
        PartitionQueryOwnedLeafRequest request, RemoteDocumentCallV1? call, CancellationToken token)
    {
        var result = call is null
            ? await ReadLocalAsync(request, expiresAt, principalId, token).ConfigureAwait(false)
            : await client.ReadLeafAsync(call, token).ConfigureAwait(false);
        return new(request.Owner, result);
    }

    internal async Task RevalidateAsync(string principalId, CancellationToken token)
    {
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        foreach (var item in original)
        { token.ThrowIfCancellationRequested(); RequireSame(item.Fence, Capture(principalId, item.Plan, item.Grant)); }
    }

    private PartitionQuerySourceFence Capture(string principalId, PartitionQueryLeafPlanV1 plan,
        ReadExecutionBudgetReadGrant grant)
        => partition.Database.WithPartitionQueryFenceView(principalId, plan.Partition, plan.Request.Query.Collection, grant,
            (view, principal, resource, digest) =>
            {
                queries.Bind(principal, resource, plan.Request);
                return new PartitionQuerySourceFence(partition.Database.CaptureRemotePartitionQuery(view, principal, plan.Partition,
                    plan.Request.Query.Collection, grant), digest);
            });

    private static void RequireSame(PartitionQuerySourceFence expected, PartitionQuerySourceFence actual)
    {
        if (!JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, FenceChanged); }
    }

    private async Task<PartitionQueryLeafResultV1> ReadLocalAsync(PartitionQueryOwnedLeafRequest request,
        DateTimeOffset expiresAt, string principalId, CancellationToken token)
    {
        var principal = partition.Database.Store.Read(view => partition.Database.Principal(view,
            principalId, partition.Database.EvaluationClock.GetUtcNow()));
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreatePartitionQueryLeaf(requestId, principal.Id,
            NativeSerialization.Serialize(request), expiresAt);
        using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, token);
        var reply = await node.ExecuteAsync(requestId, signed, command: false, cancellationToken: token).ConfigureAwait(false);
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionQueryLeafResultV1
            ?? throw Errors.Fail(ErrorCode.Corruption, FenceChanged);
    }
}
