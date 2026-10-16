using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeMarkerRules
{
    internal static void Require(RequestCqrsProbeMarkerRecord marker, RequestCqrsProbeSnapshot snapshot,
        RequestCqrsProbeLoadedArm? producerClaim, string sessionId, string voter,
        IOptions<RequestProbeExecutionOptions> executionOptions, RequestCqrsProbeRecords inventory)
    {
        if (marker.Version != RequestCqrsProbeProtocol.Version || marker.Kind != RequestCqrsProbeProtocol.MarkerKind
            || marker.SessionId != sessionId || marker.Voter != voter || marker.RequestId == Guid.Empty
            || !RequestCqrsCanonicalMarkerValidation.Valid(marker)
            || marker.Phase is not (RequestCqrsProbePhase.ProducerDisposed
                or RequestCqrsProbePhase.RequestStarted or RequestCqrsProbePhase.AuthorizationReload
                or RequestCqrsProbePhase.BeforeSubmit or RequestCqrsProbePhase.SubmitReturned
                or RequestCqrsProbePhase.ControlledDocumentGrantSettled or RequestCqrsProbePhase.ControlledDocumentOutcomeReturned
                or RequestCqrsProbePhase.CanonicalJournalFlushed or RequestCqrsProbePhase.CanonicalOutboundObserved
                or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted or RequestCqrsProbePhase.CanonicalOwnerDisposed
                or RequestCqrsProbePhase.RetireOperationSealed or RequestCqrsProbePhase.ParentStagePreflight
                or RequestCqrsProbePhase.TransferPageRetainedBudgetExceeded
                or RequestCqrsProbePhase.TransferPageReturned or RequestCqrsProbePhase.ParentTransferCloseFailed
                or RequestCqrsProbePhase.ParentReceiverIssueAcknowledged or RequestCqrsProbePhase.ParentReceiverIssueObserved
                or RequestCqrsProbePhase.ParentFinalInstallPreflight
                or RequestCqrsProbePhase.SampleChunkAdmissionPersisted or RequestCqrsProbePhase.SampleChunkNativeJobReturned
                or RequestCqrsProbePhase.SampleChunkAdmissionRefused
                or RequestCqrsProbePhase.ReplicaSnapshotReadiness
                or RequestCqrsProbePhase.EventVectorFirstDispatchReserved
                or RequestCqrsProbePhase.EventVectorFirstCapturesReady
                or RequestCqrsProbePhase.EventVectorFirstAdmitPrepared
                or RequestCqrsProbePhase.NativeModelBeforeDispatch or RequestCqrsProbePhase.NativeModelAfterDispatch or RequestCqrsProbePhase.NativeModelReplayBeforeDispatch
                or RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch
                or RequestCqrsProbePhase.ResourceOwnerInventoryOwnerValidation
                or RequestCqrsProbePhase.ReplicaEntryAcknowledged
                or RequestCqrsProbePhase.ReplicaEntryObserverDisposed or RequestCqrsProbePhase.QueryReadCutCaptured or RequestCqrsProbePhase.WaitForIndexReadCutCaptured)
            || snapshot.Markers.Any(existing => RequestCqrsProbeFiles.MarkerName(existing) == RequestCqrsProbeFiles.MarkerName(marker)))
        {
            throw Invalid();
        }
        var group = snapshot.Markers
            .Where(existing => existing.ArmId == marker.ArmId && existing.RequestId == marker.RequestId).ToArray();
        var active = snapshot.Arms.SingleOrDefault(arm => arm.Record.ArmId == marker.ArmId);
        var activeClaim = active is not null && producerClaim is not null
            && CryptographicOperations.FixedTimeEquals(active.ExactBytes, producerClaim.ExactBytes);
        var retiredProducerClaim = marker.Phase == RequestCqrsProbePhase.ProducerDisposed && producerClaim is not null
            && producerClaim.Record.ArmId == marker.ArmId && inventory.IsRetiredArm(marker.ArmId, producerClaim.ExactBytes);
        var validArm = marker.Phase == RequestCqrsProbePhase.ProducerDisposed
            ? activeClaim || retiredProducerClaim
            : active is not null && (producerClaim is null || activeClaim && CanonicalAdjunct(active.Record, marker));
        if (group.Any(existing => existing.EntryIndex != marker.EntryIndex || existing.EntryTerm != marker.EntryTerm)
            || active is not null && !RequestCqrsCanonicalScopeValidation.Matches(active.Record, marker)
            || group.Length >= executionOptions.Value.MaximumMarkersPerRequest || !validArm)
        {
            throw Invalid();
        }
    }

    internal static bool CanonicalAdjunct(RequestCqrsProbeArmRecord arm, RequestCqrsProbeMarkerRecord marker)
        => arm.Phase == RequestCqrsProbePhase.RequestStarted && arm.ReadKind == KeyLoad.Clustering.GrainReadKind.WaitForIndex
            && arm.Action == RequestCqrsProbeAction.Hold && marker.Phase == RequestCqrsProbePhase.WaitForIndexReadCutCaptured
            && marker.Outcome == RequestCqrsProbeOutcome.Observed
            || arm.Phase == RequestCqrsProbePhase.RequestStarted && arm.ReadKind == KeyLoad.Clustering.GrainReadKind.AstQuery
            && arm.Action == RequestCqrsProbeAction.Hold && marker.Phase == RequestCqrsProbePhase.QueryReadCutCaptured
            && marker.Outcome == RequestCqrsProbeOutcome.Observed
            || arm.Phase == RequestCqrsProbePhase.ReplicaEntryAcknowledged
            && marker.Phase == RequestCqrsProbePhase.ReplicaEntryObserverDisposed
            && arm.Action == RequestCqrsProbeAction.ThrowOrdinary && marker.Outcome == RequestCqrsProbeOutcome.Observed
            || arm.Phase == RequestCqrsProbePhase.CanonicalJournalFlushed && arm.Action == RequestCqrsProbeAction.Hold
            && arm.Partition is not null && marker.Outcome == RequestCqrsProbeOutcome.Observed
            && marker.Phase is RequestCqrsProbePhase.CanonicalOutboundObserved or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted or RequestCqrsProbePhase.CanonicalOwnerDisposed;

    internal static bool ValidOutcome(RequestCqrsProbeArmRecord arm, RequestCqrsProbeMarkerRecord marker)
    {
        if (marker.Phase == RequestCqrsProbePhase.ReplicaEntryObserverDisposed)
        {
            return arm.Phase == RequestCqrsProbePhase.ReplicaEntryAcknowledged && marker.Outcome == RequestCqrsProbeOutcome.Observed;
        }
        if (marker.Phase == RequestCqrsProbePhase.ProducerDisposed)
        {
            return marker.Outcome == RequestCqrsProbeOutcome.Observed;
        }
        return arm.Action == RequestCqrsProbeAction.Hold
            ? marker.Outcome is RequestCqrsProbeOutcome.Observed or RequestCqrsProbeOutcome.Released or RequestCqrsProbeOutcome.Cancelled
            : marker.Outcome == RequestCqrsProbeOutcome.FaultRequested;
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
