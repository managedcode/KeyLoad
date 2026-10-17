namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementCleanupMatrixProtocol
{
    internal const int EmptyFailureLedger = 0;
    internal const int SelectedControlVoterCount = 1;
    internal const int CanonicalGuidDigits = 32;
    internal const string EvidenceSuffix = "-denied-publication-evidence";
    internal const string MissingRefusal = "The actual original cleanup refusal was not retained.";
    internal const string FaultMismatch = "The actual admitted control fault changed its original bytes or owner.";
}

/// <summary>Each variant executes the complete original native parent and fresh-session recovery flow.</summary>
internal enum PartitionMovementCleanupMatrixFaultRole
{
    OtherKnownArm,
    ClaimedOwnerArm,
    RetainedObservedControl,
    RetiredOtherResurrection,
    ClaimedOwnerBytesChanged,
    UnfamiliarMarker,
    UnfamiliarRelease,
}

internal enum PartitionMovementCleanupMatrixFaultRepair
{
    RenameBack,
    RemoveResurrection,
    RestoreExactBytes,
    RemoveUnfamiliarCopy,
}
