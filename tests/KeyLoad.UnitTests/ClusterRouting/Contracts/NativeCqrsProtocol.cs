namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeCqrsProtocol
{
    internal const int BatchSize = 1;
    internal const int ExpectedCompletedChunks = 3;
    internal const int ExpectedDeniedChunks = 2;
    internal const int ExpectedFailureChunks = 3;
    internal const int ExpectedSettlementCount = 1;
    internal const long FirstSequence = 1;
    internal const string ProgressAlias = "KeyLoad.Tests.NativeCqrsProgress.v1";
    internal const string LeafObservationAlias = "KeyLoad.Tests.NativeCqrsLeafObservation.v1";
    internal const string ResultAlias = "KeyLoad.Tests.NativeCqrsResult.v1";
    internal const string SnapshotAlias = "KeyLoad.Tests.NativeCqrsSnapshot.v1";
    internal const string Empty = "";
    internal const string AllowedGuid = "81000000-0000-0000-0000-000000000001";
    internal const string DeniedGuid = "81000000-0000-0000-0000-000000000002";
    internal const string ConcurrentOneGuid = "81000000-0000-0000-0000-000000000003";
    internal const string ConcurrentTwoGuid = "81000000-0000-0000-0000-000000000004";
    internal const string CancellationGuid = "81000000-0000-0000-0000-000000000005";
    internal const string DisposalGuid = "81000000-0000-0000-0000-000000000006";
    internal const string HealthyAfterCancellationGuid = "81000000-0000-0000-0000-000000000007";
    internal const string HealthyAfterDisposalGuid = "81000000-0000-0000-0000-000000000008";
    internal const string FailureGuid = "81000000-0000-0000-0000-000000000009";
    internal const string Started = "started";
    internal const string ProgressOne = "progress-one-written";
    internal const string ProgressTwoAttempted = "progress-two-attempted";
    internal const string ProgressTwo = "progress-two-written";
    internal const string LeafCallAttempted = "leaf-call-attempted";
    internal const string Settled = "settled";
    internal const string GraphDenialExceptionTitle = nameof(InvalidOperationException);
    internal const int GraphDenialStatus = 500;
    internal const string FailureTitle = "operation-rejected";
    internal const string FailureDetail = "The operation was rejected after progress.";
    internal const int FailureStatus = 422;
}
