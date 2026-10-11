using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstCleanupParent
{
    internal static async Task<GrainOperationReply> ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, IOptions<NodeOptions> options,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, DecodedGrainRequest parent,
        EventFeedControlRequest request, string principalId, PhysicalOwnerDirectoryV1 directory,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        EventVectorCurrentSourceRound? fresh = null;
        EventVectorPreparedFirstCleanup? prepared = null;
        GrainOperationReply? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fresh = await EventVectorCurrentSourceRound.CreateAsync(runtime, node, partition, options.Value,
                    directory, request, principalId, parent.Envelope.ExpiresAt, admission, encoding,
                    work, cancellationToken).ConfigureAwait(false);
                prepared = partition.Database.PrepareVerifiedEventVectorFirstCleanup(principalId, request,
                    directory.ControlOwner, parent.Envelope.ExpiresAt, fresh.Witness, work, cancellationToken);
                await EventVectorParentControlCall.SubmitWithoutDecodingAsync(node, partition, clock,
                    parent, prepared.Phase, work, cancellationToken).ConfigureAwait(false);
                result = await EventVectorParentTerminalObservation.ReadAsync(runtime, node, partition,
                    options.Value, request, principalId, directory, parent.Envelope.ExpiresAt,
                    admission, encoding, routing, work, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (prepared is not null)
            { ServerFailureObserver.Observe(prepared.Dispose, failures); }
            if (fresh is not null)
            { ServerFailureObserver.Observe(fresh.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable);
    }
}
