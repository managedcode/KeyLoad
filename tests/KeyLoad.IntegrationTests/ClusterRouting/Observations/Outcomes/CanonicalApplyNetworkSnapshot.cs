namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record CanonicalApplyNetworkSnapshot(CanonicalApplyNetworkPhase Phase, TaskStatus? OriginalSdk,
    bool? OriginalSdkSucceeded, ErrorCode? OriginalSdkError,
    bool CallerCanceled, bool ScenarioCanceled, bool FlushMarkerObserved, bool OriginalReceiptObserved,
    DateTimeOffset ObservedAt);
