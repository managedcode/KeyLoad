using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorControlFields
{
    private const int Version = 1;
    private const long Initial = 0;
    private const long FirstRevision = 1;
    private const string InvalidPhase = "The native event vector control fields are inconsistent.";

    internal static void Require(EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(admission);
        var request = phase.OriginalRequest;
        if (request is null || phase.Version != Version || request.Version != Version
            || request.CommandId == Guid.Empty || request.MapId == Guid.Empty
            || request.ControlPartition is null || request.Scope is null
            || phase.OriginalParentExpiresAt == default
            || !Enum.IsDefined(request.Action) || !Enum.IsDefined(request.Start)
            || !Enum.IsDefined(phase.Action) || phase.ExpectedCleanupGeneration < Initial
            || phase.ExpectedCleanupGeneration != Initial && request.Action != EventFeedControlAction.Release)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        admission.RequireEncodedBytes(NativeSerialization.Measure(phase));
        _ = EventVectorCoverageWitnessEncoding.Read(phase.OriginalCoverageWitness, request, admission);
        if (!phase.OriginalCleanupFrontierBytes.IsEmpty
            && request.Action != EventFeedControlAction.Release)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        RequireRevision(phase);
        RequireAction(phase);
        RequireSlots(phase, admission);
    }

    private static void RequireRevision(EventVectorControlPhase phase)
    {
        var initial = phase.Action == EventVectorControlAction.Open;
        if (initial && (phase.ExpectedMapRevision != Initial || phase.ExpectedCoverageGeneration != Initial
                || phase.ExpectedCleanupGeneration != Initial)
            || !initial && (phase.ExpectedMapRevision < FirstRevision
                || phase.ExpectedCoverageGeneration < FirstRevision))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
    }

    private static void RequireAction(EventVectorControlPhase phase)
    {
        var action = phase.OriginalRequest.Action;
        var matches = phase.Action switch
        {
            EventVectorControlAction.Open or EventVectorControlAction.PublishOpen => action == EventFeedControlAction.Open,
            EventVectorControlAction.Offer => action is EventFeedControlAction.Offer or EventFeedControlAction.Tail,
            EventVectorControlAction.PublishAcknowledgement => action == EventFeedControlAction.Acknowledge,
            EventVectorControlAction.PublishRefresh => action == EventFeedControlAction.Refresh,
            EventVectorControlAction.CompleteRelease => action == EventFeedControlAction.Release,
            EventVectorControlAction.AdmitSource or EventVectorControlAction.ObserveSource =>
                action is EventFeedControlAction.Open or EventFeedControlAction.Acknowledge
                    or EventFeedControlAction.Refresh or EventFeedControlAction.Release,
            _ => false
        };
        if (!matches)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
    }

    private static void RequireSlots(EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        var initial = phase.Action == EventVectorControlAction.Open;
        var intended = phase.Action == EventVectorControlAction.AdmitSource;
        var observed = phase.Action == EventVectorControlAction.ObserveSource;
        var entries = phase.Action is EventVectorControlAction.Open or EventVectorControlAction.PublishOpen
            or EventVectorControlAction.Offer
            or EventVectorControlAction.PublishAcknowledgement or EventVectorControlAction.PublishRefresh
            or EventVectorControlAction.CompleteRelease;
        if (!initial && intended != (phase.IntendedSourcePhase is not null)
            || observed != (phase.ObservedSourcePhase is not null)
            || observed != phase.ExpectedPendingSourcePhaseId.HasValue
            || entries != !phase.OriginalEncodedEntries.IsEmpty
            || entries != !phase.OriginalEntryChecksum.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        if (entries)
        {
            if (phase.OriginalEntryChecksum.Length != SHA256.HashSizeInBytes)
            { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
            var original = EventVectorEntryEncoding.Decode(phase.OriginalEncodedEntries, phase.OriginalEntryChecksum, admission);
            if (initial && original.IsEmpty != (phase.IntendedSourcePhase is null))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidPhase); }
        }
        EventVectorControlSourceFields.Require(phase, admission);
    }
}
