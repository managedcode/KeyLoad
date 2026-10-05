namespace KeyLoad.Comparisons;

/// <summary>Owns stable safe diagnostic identities for open-loop failures.</summary>
internal static class OpenLoopFailureCodes
{
    internal const string OpenLoopObservedTopologyMismatch = "OpenLoopObservedTopologyMismatch";
    internal const string OpenLoopAccountingInvariantFailed = "OpenLoopAccountingInvariantFailed";
    internal const string OpenLoopEvidenceIdentityInvalid = "OpenLoopEvidenceIdentityInvalid";
    internal const string OpenLoopEvidenceSampleSelectionInvalid = "OpenLoopEvidenceSampleSelectionInvalid";
    internal const string OpenLoopCancellationProofInvalid = "OpenLoopCancellationProofInvalid";
    internal const string OpenLoopCancellationProofIdentityInvalid = "OpenLoopCancellationProofIdentityInvalid";
    internal const string OpenLoopCancellationMilestoneInvalid = "OpenLoopCancellationMilestoneInvalid";
    internal const string OpenLoopCancellationAccountingInvalid = "OpenLoopCancellationAccountingInvalid";
    internal const string OpenLoopCancellationHealthReadUnavailable = "OpenLoopCancellationHealthReadUnavailable";
    internal const string OpenLoopCancellationHealthyReadMissing = "OpenLoopCancellationHealthyReadMissing";
    internal const string OpenLoopCancellationHealthyReadMismatch = "OpenLoopCancellationHealthyReadMismatch";
    internal const string OpenLoopCancellationMarkerInvalid = "OpenLoopCancellationMarkerInvalid";
    internal const string OpenLoopCancellationControlAlreadyExists = "OpenLoopCancellationControlAlreadyExists";
    internal const string OpenLoopCancellationControlInvalid = "OpenLoopCancellationControlInvalid";
    internal const string OpenLoopCancellationMilestoneMissing = "OpenLoopCancellationMilestoneMissing";
    internal const string OpenLoopCancellationWasNotObserved = "OpenLoopCancellationWasNotObserved";
    internal const string OpenLoopMeasurementNotStarted = "OpenLoopMeasurementNotStarted";
    internal const string OpenLoopMeasurementFailed = "OpenLoopMeasurementFailed";
    internal const string OpenLoopWorkerStoppedBeforeSchedule = "OpenLoopWorkerStoppedBeforeSchedule";
    internal const string OpenLoopEvidenceByteLimitExceeded = "OpenLoopEvidenceByteLimitExceeded";
}
