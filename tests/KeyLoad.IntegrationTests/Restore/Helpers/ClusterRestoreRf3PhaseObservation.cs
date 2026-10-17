using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Records only closed native lifecycle phases; the observed phase does not establish a failure cause.</summary>
internal sealed class ClusterRestoreRf3PhaseObservation
{
    private readonly TimeProvider clock;
    private readonly long started;

    internal ClusterRestoreRf3PhaseObservation(TimeProvider clock)
    {
        this.clock = clock;
        started = clock.GetTimestamp();
    }

    internal void Set(ClusterRestoreRf3Phase value) => Current = value;

    internal void Startup(string value) => Current = value switch
    {
        nameof(ClusterRestoreRf3Phase.ProfileCreate) => ClusterRestoreRf3Phase.ProfileCreate,
        nameof(ClusterRestoreRf3Phase.VerifiedImageRead) => ClusterRestoreRf3Phase.VerifiedImageRead,
        nameof(ClusterRestoreRf3Phase.BuilderCreate) => ClusterRestoreRf3Phase.BuilderCreate,
        nameof(ClusterRestoreRf3Phase.OriginalParameters) => ClusterRestoreRf3Phase.OriginalParameters,
        nameof(ClusterRestoreRf3Phase.OriginalPeerKey) => ClusterRestoreRf3Phase.OriginalPeerKey,
        nameof(ClusterRestoreRf3Phase.IsolationPrepare) => ClusterRestoreRf3Phase.IsolationPrepare,
        nameof(ClusterRestoreRf3Phase.BuildApplication) => ClusterRestoreRf3Phase.BuildApplication,
        nameof(ClusterRestoreRf3Phase.VerifyImageBeforeStart) => ClusterRestoreRf3Phase.VerifyImageBeforeStart,
        nameof(ClusterRestoreRf3Phase.ApplicationStart) => ClusterRestoreRf3Phase.ApplicationStart,
        nameof(ClusterRestoreRf3Phase.SixHealthy) => ClusterRestoreRf3Phase.SixHealthy,
        nameof(ClusterRestoreRf3Phase.DataReadiness) => ClusterRestoreRf3Phase.DataReadiness,
        nameof(ClusterRestoreRf3Phase.VerifyStartedImages) => ClusterRestoreRf3Phase.VerifyStartedImages,
        nameof(ClusterRestoreRf3Phase.StartupReady) => ClusterRestoreRf3Phase.StartupReady,
        _ => ClusterRestoreRf3Phase.Unknown
    };

    internal ClusterRestoreRf3Phase Current
    {
        get; private set;
    }

    internal async Task WriteAsync(ClusterRestoreRf3Phase bodyPhase, int failures,
        bool callerCanceled, bool deadlineCanceled)
    {
        var evidence = new ClusterRestoreRf3PhaseEvidence(bodyPhase, Current, failures, callerCanceled,
            deadlineCanceled, clock.GetElapsedTime(started), ClusterRestoreRf3FailureCause.Unknown);
        await TestContext.Current!.OutputWriter.WriteLineAsync(JsonSerializer.Serialize(evidence)).ConfigureAwait(false);
    }
}
