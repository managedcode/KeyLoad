using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorParentCapture
{
    internal static async Task<EventVectorCapturedExchange> ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options,
        RegisteredPhysicalOwnerV1 destination, EventVectorCoverageCall original,
        DateTimeOffset originalExpiry, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        using var call = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.Coverage, original, originalExpiry, work, encoding);
        var localOwner = PhysicalOwnerConfiguredTuples.Local(options, partition).Owner;
        using var exchange = PhysicalOwnerEntryValidation.SameOwner(localOwner, destination.Owner)
            ? await runtime.SendLocalEventVectorAsync(call.OriginalCall, work, cancellationToken).ConfigureAwait(false)
            : await runtime.SendRemoteEventVectorAsync(destination, call.OriginalCall,
                encoding, work, cancellationToken).ConfigureAwait(false);
        EventVectorCoveragePacketOwner? packet = null;
        EventVectorCoverageCapture? capture = null;
        try
        {
            packet = EventVectorCoveragePacketOwner.Create(exchange, work);
            capture = EventVectorCoverageCaptureAdmission.Read(packet.OriginalPacket, original,
                destination, admission, work);
            work.Check();
            var result = new EventVectorCapturedExchange(packet, capture);
            packet = null;
            capture = null;
            return result;
        }
        finally
        {
            EventVectorCaptureBuffers.Clear(capture);
            packet?.Dispose();
        }
    }
}
