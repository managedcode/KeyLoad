using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorCoverageFetch
{
    internal static async Task<EventVectorCapturedExchange> ExecuteAsync(RemoteDocumentClient client,
        OrleansNode node, PartitionHost partition, NodeOptions options,
        EventVectorCoverageCall originalRequest, RegisteredPhysicalOwnerV1 destination,
        DateTimeOffset originalExpiresAt, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work,
        CancellationToken originalCancellationToken)
    {
        originalCancellationToken.ThrowIfCancellationRequested();
        using var call = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.Coverage, originalRequest, originalExpiresAt, work, encoding);
        using var exchange = await client.SendEventVectorAsync(destination, call.OriginalCall,
            encoding, work, originalCancellationToken).ConfigureAwait(false);
        EventVectorCoveragePacketOwner? packet = null;
        EventVectorCoverageCapture? capture = null;
        try
        {
            packet = EventVectorCoveragePacketOwner.Create(exchange, work);
            capture = EventVectorCoverageCaptureAdmission.Read(packet.OriginalPacket, originalRequest,
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
