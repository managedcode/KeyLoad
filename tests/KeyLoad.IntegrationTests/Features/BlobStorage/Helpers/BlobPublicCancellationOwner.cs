using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobPublicCancellationOwner
{
    internal string Root = string.Empty;
    internal bool RootOwned;
    internal bool WaveStartupAttempted;
    internal string ImageDataRoot = string.Empty;
    internal global::KeyLoad.IntegrationTests.Features.ClusterReplication.LocalRf3ImageTestSession? LocalImageSession;
    internal RequestCqrsProbeFixture Controls = null!;
    internal RequestCqrsRf3Wave Wave = null!;
    internal RequestCqrsRf3Callers Administrator = null!;
    internal RequestCqrsRf3Callers Caller = null!;
    internal RequestCqrsPhaseFaultIdentity Identity = null!;
    internal IReadOnlyList<ReplicaSiloDiscovery> Discovery = [];
    internal readonly RequestCqrsLifecycleEvidence Lifecycle = new();
    internal readonly List<Guid> Arms = [];
    internal readonly List<CancellationTokenSource> CallCancellations = [];
    internal CancellationToken ParentToken;
    internal Task? SdkCall;
    internal Task<RequestCqrsFaultMcpObservation>? McpCall;
    internal CancellationTokenSource? ScenarioDeadline;

    internal static async Task RunAsync(bool useMcp, CancellationToken token)
    {
        using var parentTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, parentTimeout.Token);
        using var waveTimeout = new CancellationTokenSource(RequestCqrsRf3Protocol.WaveDeadline, TimeProvider.System);
        var owner = new BlobPublicCancellationOwner { ParentToken = parent.Token };
        var failures = new List<Exception>();
        owner.Lifecycle.SetTokens(token, parent.Token, default);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner.ScenarioDeadline = CancellationTokenSource.CreateLinkedTokenSource(parent.Token, waveTimeout.Token);
            owner.Lifecycle.SetWaveToken(owner.ScenarioDeadline.Token);
            await BlobPublicCancellationSetup.StartAsync(owner, owner.ScenarioDeadline.Token);
            await BlobPublicCancellationTrial.RunAsync(owner, useMcp, owner.ScenarioDeadline.Token);
        }, failures);
        owner.Lifecycle.RecordFirstFailureIfAny(failures);
        await owner.JoinAsync(failures);
        owner.Lifecycle.RecordTerminal();
        owner.Lifecycle.ThrowWithContext(failures);
    }

    private async Task JoinAsync(List<Exception> failures)
    {
        var cleanupStart = failures.Count;
        foreach (var cancellation in CallCancellations)
        {
            await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures);
        }
        await RequestCqrsPhaseFaultCleanup.RunAsync(Root, RootOwned && LocalImageSession is null, Controls, Wave, WaveStartupAttempted,
            Caller, Administrator, Discovery.Count == 0 ? null : Discovery, null, ScenarioDeadline, SdkCall, McpCall,
            Arms.FirstOrDefault(), failures, Arms.Skip(1).ToArray());
        foreach (var cancellation in CallCancellations)
        {
            ServerFailureObserver.Observe(cancellation.Dispose, failures);
        }
        await JoinImageAsync(cleanupStart, failures);
    }

    private async Task JoinImageAsync(int cleanupStart, List<Exception> failures)
    {
        if (LocalImageSession is null)
        {
            return;
        }
        var cleanup = failures.Skip(cleanupStart).ToList();
        var prior = cleanup.Count;
        var stopped = (Wave is not null && cleanup.Count == 0) || !WaveStartupAttempted;
        await global::KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterFixtureLocalImageLifecycle
            .JoinAsync(LocalImageSession, ImageDataRoot, stopped, !WaveStartupAttempted, cleanup);
        failures.AddRange(cleanup.Skip(prior));
        if (RootOwned && stopped && cleanup.Count == 0)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(Root, recursive: true), failures);
        }
    }
}
