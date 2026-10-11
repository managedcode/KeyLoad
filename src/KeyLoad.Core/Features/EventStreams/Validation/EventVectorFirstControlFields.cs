namespace KeyLoad.Core;

internal static class EventVectorFirstControlFields
{
    private const long InitialRevision = 0;
    private const string Invalid = "The original event vector first issuance fields are inconsistent.";

    internal static bool IsIssuance(EventVectorControlAction action)
        => action is EventVectorControlAction.AdmitFirstIntent
            or EventVectorControlAction.CaptureFirstPhase or EventVectorControlAction.ReserveFirstDispatch
            or EventVectorControlAction.AbandonFirstIssuance or EventVectorControlAction.CleanupAbandonedIssuance;

    internal static void Require(EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        var request = phase.OriginalRequest;
        var firstOwner = phase.Action is EventVectorControlAction.Open or EventVectorControlAction.AdmitFirstIntent
            or EventVectorControlAction.CaptureFirstPhase or EventVectorControlAction.ReserveFirstDispatch;
        if (firstOwner != (phase.OriginalParentRequestId != Guid.Empty))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var cleanup = phase.Action is EventVectorControlAction.AbandonFirstIssuance
            or EventVectorControlAction.CleanupAbandonedIssuance;
        if (cleanup != (request.OriginalFirstIssuancePublicCommandId != Guid.Empty)
            || cleanup && (request.Action != EventFeedControlAction.Release
                || request.CommandId == request.OriginalFirstIssuancePublicCommandId
                || request.Cursor is not null || request.OfferDigest is not null
                || request.ExpectedRevision != InitialRevision
                || request.ExpectedCoverageGeneration != InitialRevision
                || phase.OriginalFirstIssuancePublicCommandId != request.OriginalFirstIssuancePublicCommandId))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        if (IsIssuance(phase.Action)
            && (phase.ExpectedMapRevision != InitialRevision
                || phase.ExpectedCoverageGeneration != InitialRevision
                || phase.ExpectedCleanupGeneration != InitialRevision
                || phase.IntendedSourcePhase is not null || phase.ObservedSourcePhase is not null
                || phase.ExpectedPendingSourcePhaseId is not null
                || !phase.OriginalEncodedEntries.IsEmpty || !phase.OriginalEntryChecksum.IsEmpty
                || !phase.OriginalCleanupFrontierBytes.IsEmpty))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        if (!cleanup)
        { EventVectorFirstPhaseCarrier.Require(phase, admission); }
    }
}

