using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorOfferPageProof
{
    private const string Invalid = "The original complete event vector page result requires recovery.";

    internal static void Require(EventVectorControlPhase phase, ImmutableArray<EventVectorEntry> acknowledged,
        ImmutableArray<EventVectorEntry> offered, EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(phase.OriginalSourcePageBytes.Length);
        if (phase.OriginalSourcePageBytes.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var pages = NativeSerialization.Deserialize<ImmutableArray<EventSourcePage>>(phase.OriginalSourcePageBytes.Span);
        var derived = EventVectorOfferPageProjection.Create(acknowledged, pages, admission);
        if (!derived.SequenceEqual(offered))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
    }

    internal static void RequireResult(EventVectorControlResult result, EventVectorControlPhase phase,
        EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(result.OriginalSourcePageBytes.Length);
        if (!result.OriginalSourcePageBytes.Span.SequenceEqual(phase.OriginalSourcePageBytes.Span))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
    }
}
