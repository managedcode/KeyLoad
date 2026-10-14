using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorRefreshParent
{
    internal static async Task<GrainOperationReply> ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, IOptions<NodeOptions> options,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, DecodedGrainRequest parent,
        EventFeedControlRequest request, string principalId, PhysicalOwnerDirectoryV1 directory,
        EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        EventVectorCurrentSourceRound? fresh = null;
        EventVectorPreparedRefresh? prepared = null;
        GrainOperationReply? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                fresh = await EventVectorCurrentSourceRound.CreateAsync(runtime, node, partition, options.Value,
                    directory, request, principalId, parent.Envelope.ExpiresAt, admission, encoding,
                    work, cancellationToken).ConfigureAwait(false);
                prepared = partition.Database.PrepareVerifiedEventVectorRefresh(principalId, request,
                    directory.ControlOwner, fresh.Witness, parent.Envelope.ExpiresAt, parent.Envelope.RequestId,
                    work, cancellationToken);
                await EventVectorRefreshCompletion.ExecuteAsync(runtime, node, partition, options.Value,
                    clock, parent, prepared, admission, encoding, work, cancellationToken).ConfigureAwait(false);
                result = await EventVectorParentTerminalObservation.ReadAsync(runtime, node, partition,
                    options.Value, request, principalId, directory, parent.Envelope.ExpiresAt,
                    admission, encoding, routing, work, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (prepared is not null)
            {
                ServerFailureObserver.Observe(prepared.Dispose, failures);
            }
            if (fresh is not null)
            {
                ServerFailureObserver.Observe(fresh.Dispose, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable);
    }
}
