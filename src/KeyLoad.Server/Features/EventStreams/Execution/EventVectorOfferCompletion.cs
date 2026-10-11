using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorOfferCompletion
{
    private const int FirstOrdinal = 0;

    internal static async Task ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, NodeOptions options, TimeProvider clock,
        DecodedGrainRequest parent, EventVectorPreparedOffer prepared,
        ImmutableArray<RegisteredPhysicalOwnerV1> destinations, EventVectorAdmissionPolicy admission,
        EventVectorRemoteEncoding encoding, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var request = GrainNativePayload.ReadCommand<EventFeedControlRequest>(parent.Payload);
        var principal = parent.Envelope.PrincipalId
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid);
        if (prepared.Entries.Length != prepared.OriginalPinPhaseBytes.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, EventVectorRemoteProtocol.Invalid); }
        admission.RequireEntryCount(prepared.Entries.Length);
        var pages = ImmutableArray.CreateBuilder<EventSourcePage>(prepared.Entries.Length);
        for (var ordinal = FirstOrdinal; ordinal < prepared.Entries.Length; ordinal++)
        {
            work.Check();
            var bytes = prepared.OriginalPinPhaseBytes[ordinal];
            admission.RequireEncodedBytes(bytes.Length);
            using var pin = new EventVectorOpenPinOwner(NativeSerialization.Deserialize<EventVectorSourcePhase>(bytes.Span));
            var destination = destinations.Single(owner =>
                PhysicalOwnerEntryValidation.SameOwner(owner.Owner, pin.Phase.SourceOwner));
            var page = await EventVectorOfferSourcePages.ReadAsync(runtime, node, partition, options,
                destination, principal, bytes, prepared.Entries[ordinal], request.Limit,
                parent.Envelope.ExpiresAt, admission, encoding, work, cancellationToken).ConfigureAwait(false);
            var length = NativeSerialization.Measure(page);
            admission.RequireEncodedBytes(length);
            work.ChargeBytes(length);
            pages.Add(page);
        }
        using var owned = EventVectorOfferPhaseOwner.Create(request, parent, prepared,
            pages.MoveToImmutable(), admission, work);
        await EventVectorParentControlCall.SubmitWithoutDecodingAsync(node, partition, clock, parent,
            owned.Phase, work, cancellationToken).ConfigureAwait(false);
        work.Check();
    }
}
