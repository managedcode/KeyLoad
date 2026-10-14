using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstCleanupPublicResult
{
    private const int Version = 1;
    private const long NoPublishedRevision = 0;

    internal static EventVectorFirstIssuanceResult Retain(IAtomicTransaction transaction,
        PrincipalRecord principal, EventVectorControlPhase phase, ReplicatedOperation operation,
        EventVectorFirstIssuanceResult result, EventVectorAdmissionPolicy admission)
    {
        if (phase.Action is not (EventVectorControlAction.AbandonFirstIssuance
            or EventVectorControlAction.CleanupAbandonedIssuance))
        {
            return result;
        }
        var index = EventVectorFirstCleanupLocator.Create(principal, phase, operation, result.Issuance, admission);
        var released = result.Issuance.State == EventVectorFirstIssuanceState.Released;
        var value = new EventFeedControlResult
        {
            Version = Version,
            PublicCommandId = phase.OriginalRequest.CommandId,
            MapId = result.Issuance.MapId,
            Revision = NoPublishedRevision,
            CoverageGeneration = NoPublishedRevision,
            Released = released,
            Retained = !released,
            Cursor = null,
            OfferDigest = null,
            Pages = ImmutableArray<EventSourcePage>.Empty,
            Receipt = result.Receipt
        };
        admission.RequireEncodedBytes(NativeSerialization.Measure(value));
        var original = NativeSerialization.Serialize(value);
        admission.RequireEncodedBytes(original.Length);
        var retained = result with
        {
            OriginalPublicResultBytes = original
        };
        admission.RequireEncodedBytes(NativeSerialization.Measure(retained));
        EventVectorFirstCleanupLocator.Write(transaction, index, admission);
        return retained;
    }
}
