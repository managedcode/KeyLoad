using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal enum CanonicalApplyNetworkPhase
{
    NotAdmitted, PriorProfile, ImageProof, NativeWaveStartup, SignedDiscovery, AdminConnection,
    PersistedIdentity, LeaderStatus, SeedState, CallerConnection, OriginalSdkAdmission,
    BeforeSubmitObserved, BeforeSubmitReleased, CanonicalFlushObserved, OriginalSdkReceipt,
    OriginalProducerSettled, IndependentAppendObserved, CanonicalReleased, CanonicalOwnerDisposed,
    ArmRetirement, HealthyReplay, Completed
}

internal sealed record CanonicalApplyNetworkSnapshot(CanonicalApplyNetworkPhase Phase, TaskStatus? OriginalSdk,
    bool? OriginalSdkSucceeded, ErrorCode? OriginalSdkError,
    bool CallerCanceled, bool ScenarioCanceled, bool FlushMarkerObserved, bool OriginalReceiptObserved,
    DateTimeOffset ObservedAt);

internal static class CanonicalApplyNetworkEvidence
{
    private static readonly TimeProvider ObservationClock = TimeProvider.System;
    private const int MaximumBytes = 2_048;
    private const int Version = 1;
    private const string ArtifactPrefix = "c1-canonical-native-phase-";
    private const string ArtifactSuffix = ".json";
    private const string ArtifactDataKey = "KeyLoad.C1.CanonicalNativePhaseArtifact";

    internal static CanonicalApplyNetworkSnapshot Capture(CanonicalApplyNetworkPhase phase,
        Task<Result<CommitReceipt>>? original, bool callerCanceled, bool scenarioCanceled,
        bool markerObserved, bool receiptObserved)
    {
        bool? succeeded = null;
        ErrorCode? error = null;
        if (original is { IsCompletedSuccessfully: true })
        {
            var outcome = original.GetAwaiter().GetResult();
            succeeded = outcome.IsSuccess;
            if (outcome.Problem?.ErrorCode is { } text && Enum.TryParse<ErrorCode>(text, out var code)
                && Enum.IsDefined(code) && string.Equals(text, code.ToString(), StringComparison.Ordinal))
            { error = code; }
        }
        return new(phase, original?.Status, succeeded, error, callerCanceled, scenarioCanceled,
            markerObserved, receiptObserved, ObservationClock.GetUtcNow());
    }

    internal static void Save(CanonicalApplyNetworkSnapshot original, TaskStatus? terminal, IReadOnlyList<Exception> failures)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version, Original = original, AfterCleanupOriginalSdk = terminal });
        if (bytes.Length > MaximumBytes)
        { throw new InvalidDataException("The closed C1 canonical phase evidence exceeds its original context bound."); }
        var directory = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, "artifacts", "qualification");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ArtifactPrefix + Guid.NewGuid().ToString("N") + ArtifactSuffix);
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(true); }
        foreach (var failure in failures)
        { failure.Data[ArtifactDataKey] = path; }
    }
}
