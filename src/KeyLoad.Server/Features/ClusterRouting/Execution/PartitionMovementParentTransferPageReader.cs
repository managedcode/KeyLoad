using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentTransferPageReader
{
    private readonly PartitionMovementReceiver receiver;
    private readonly PartitionMovementClient client;
    private readonly TimeProvider clock;
    private readonly IOptions<GrainRoutingOptions> routing;

    internal PartitionMovementParentTransferPageReader(PartitionMovementReceiver receiver, PartitionMovementClient client, TimeProvider clock, IOptions<GrainRoutingOptions> routing)
    {
        this.receiver = receiver;
        this.client = client;
        this.clock = clock;
        this.routing = routing;
    }

    internal async Task<PartitionMoveImagePage> ReadOriginalPageAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, int ordinal, ReadExecutionBudget work,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        var capture = state.Selected!;
        var expiry = PartitionMovementParentDeadline.Expiry(work, clock, routing);
        var query = new PartitionMovementTransferAuthorityQuery(request, capture.OriginalPhaseCommandId,
            work.RemainingReadGrantBytes, work.RemainingReadGrantRecords, work.MaximumResultBytes);
        var authority = await receiver.ReadTransferAuthorityAsync(principalId, query, expiry, work,
            cancellationToken).ConfigureAwait(false);
        var opened = await client.ReadTransferDataAsync(principalId, authority,
            PartitionMovementTransferDataAction.Open, Guid.Empty, PartitionMoveProtocol.EmptyCount, expiry,
            work, cancellationToken).ConfigureAwait(false);
        var handle = ReadTransferResult(opened).Handle
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        var failures = new List<Exception>();
        PartitionMoveImagePage? page = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (handle.HandleId == Guid.Empty || handle.ExpiresAt > expiry
                || !NativeSerialization.Serialize(handle.OriginalDescriptor).AsSpan()
                    .SequenceEqual(NativeSerialization.Serialize(capture.OriginalDescriptor))
                || handle.PageCount != capture.OriginalDescriptor!.Families.Sum(static family => family.PageCount)
                || ordinal < PartitionMoveProtocol.EmptyCount || ordinal >= handle.PageCount)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof); }
            var reply = await client.ReadTransferDataAsync(principalId, authority,
                PartitionMovementTransferDataAction.Page, handle.HandleId, ordinal, expiry, work,
                cancellationToken).ConfigureAwait(false);
            var actual = ReadTransferResult(reply).Page
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
            if (actual.HandleId != handle.HandleId || actual.Ordinal != ordinal)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof); }
            page = NativeSerialization.Deserialize<PartitionMoveImagePage>(actual.NativePage.Span);
            if (page.MoveId != request.MoveId || page.Partition != request.Partition || page.Ordinal != ordinal)
            { throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof); }
        }, failures).ConfigureAwait(false);
        var beforeCloseFailures = failures.Count;
        await ServerFailureObserver.ObserveAsync(() => CloseOriginalAsync(principalId, authority,
            handle, expiry, work, cancellationToken), failures).ConfigureAwait(false);
        if (failures.Count > beforeCloseFailures && phaseObservation is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => phaseObservation(
                GrainRequestPhase.ParentTransferCloseFailed, cancellationToken).AsTask(), failures).ConfigureAwait(false);
        }
        // Failed close retains its actual charged owner; only real Abort/SourceBeginAbort or shutdown joins it.
        ServerFailureObserver.ThrowIfAny(failures);
        return page ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }

    private async Task CloseOriginalAsync(string principalId, PartitionMovementAuthenticatedAuthority authority,
        PartitionMovementTransferHandle handle, DateTimeOffset expiry, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var closed = await client.ReadTransferDataAsync(principalId, authority,
            PartitionMovementTransferDataAction.Close, handle.HandleId, PartitionMoveProtocol.EmptyCount,
            expiry, work, cancellationToken).ConfigureAwait(false);
        var actual = ReadTransferResult(closed).Closed;
        if (actual?.HandleId != handle.HandleId
            || actual.Disposition != PartitionMovementTransferCloseDisposition.Joined)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
    }

    private static PartitionMovementTransferDataResult ReadTransferResult(GrainOperationReply reply)
    {
        if (reply.Error is { } code)
        { throw Errors.Fail(code, reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMovementTransferDataResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
    }
}
