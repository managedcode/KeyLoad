using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorOfferSourcePages
{
    private const int Version = 1;
    private const int SinglePage = 1;
    private const int FirstOrdinal = 0;

    internal static async Task<EventSourcePage> ReadAsync(RemoteDocumentRuntime runtime, OrleansNode node,
        PartitionHost partition, NodeOptions options, RegisteredPhysicalOwnerV1 destination,
        string principalId, ReadOnlyMemory<byte> originalPinBytes, EventVectorEntry acknowledged,
        int limit, DateTimeOffset parentExpiry, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        admission.RequireEncodedBytes(originalPinBytes.Length);
        using var originalPinOwner = new EventVectorOpenPinOwner(
            NativeSerialization.Deserialize<EventVectorSourcePhase>(originalPinBytes.Span));
        var originalPin = originalPinOwner.Phase;
        _ = EventVectorSourceMutationValidation.ReadOriginal(originalPin, admission);
        if (originalPin.Source != acknowledged.Source || originalPin.PrincipalId != principalId
            || !PhysicalOwnerEntryValidation.SameOwner(originalPin.SourceOwner, destination.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, EventVectorRemoteProtocol.Invalid); }
        using var observationCall = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.OriginalOutcome, originalPin, parentExpiry, work, encoding);
        using var original = await SendAsync(runtime, partition, options, destination,
            observationCall.OriginalCall, encoding, work, cancellationToken).ConfigureAwait(false);
        using var pin = EventVectorOfferPinObservation.Read(originalPin, acknowledged, observationCall.OriginalCall,
            original.Reply, originalPinBytes, admission, work);
        var read = new ReadEventSourceRequest(acknowledged.Source, acknowledged.Position, limit);
        var input = new EventVectorSourceReadCall(Version, principalId,
            new EventVectorSourcesReadRequest(Version, [read], destination.Owner, [pin.Carrier]));
        using var readCall = EventVectorOutgoingCall.Create(node, partition, options,
            EventVectorRemoteAction.SourceRead, input, parentExpiry, work, encoding);
        using var actual = await SendAsync(runtime, partition, options, destination,
            readCall.OriginalCall, encoding, work, cancellationToken).ConfigureAwait(false);
        work.ChargeBytes(actual.Reply.Reply.OriginalNativeReply.Length);
        var pages = EventVectorAuthenticatedSourcePages.Read(actual.Reply, readCall.OriginalCall, encoding, admission);
        if (pages.Length != SinglePage)
        { throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid); }
        work.Check();
        return pages[FirstOrdinal];
    }

    private static Task<EventVectorAuthenticatedExchange> SendAsync(RemoteDocumentRuntime runtime,
        PartitionHost partition, NodeOptions options, RegisteredPhysicalOwnerV1 destination,
        EventVectorRemoteCall call, EventVectorRemoteEncoding encoding, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var local = PhysicalOwnerConfiguredTuples.Local(options, partition).Owner;
        return PhysicalOwnerEntryValidation.SameOwner(local, destination.Owner)
            ? runtime.SendLocalEventVectorAsync(call, work, cancellationToken)
            : runtime.SendRemoteEventVectorAsync(destination, call, encoding, work, cancellationToken);
    }
}
