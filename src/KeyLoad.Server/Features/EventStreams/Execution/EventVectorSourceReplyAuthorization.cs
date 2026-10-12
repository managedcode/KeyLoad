using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorSourceReplyAuthorization
{
    internal static async Task<EventVectorSourcePhase> ReadAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, TimeProvider clock,
        DecodedGrainRequest parent, EventVectorControlPhase control, EventVectorSourcePhase admitted,
        EventVectorRemoteCall call, EventVectorMacCheckedReply reply, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        EventVectorCurrentSourceRound? fresh = null;
        EventVectorSourcePhase? result = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var principal = GrainRequestAuthority.ReloadForRequest(partition.Database, parent.Envelope, clock);
                var directory = partition.Database.ReadEventVectorConfiguredOwners(principal.Id,
                    control.OriginalRequest, work, cancellationToken);
                fresh = await EventVectorCurrentSourceRound.CreateAsync(runtime, node, partition, options,
                    directory, control.OriginalRequest, principal.Id, parent.Envelope.ExpiresAt,
                    admission, encoding, work, cancellationToken).ConfigureAwait(false);
                work.Check();
                result = EventVectorAuthenticatedSourceObservation.Read(admitted, call, reply, admission);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (fresh is not null)
            { ServerFailureObserver.Observe(fresh.Dispose, failures); }
        }
        try
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return result ?? throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid);
        }
        catch (Exception error) when (result is null && call.Action == EventVectorRemoteAction.SourceEffect
            && global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, EventVectorRemoteProtocol.Unavailable, error); }
    }
}
