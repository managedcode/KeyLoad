using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobPublicCancellationOwner
{
    internal string Root = string.Empty;
    internal bool RootOwned;
    internal bool WaveStartupAttempted;
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
        foreach (var cancellation in CallCancellations)
        { await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures); }
        await RequestCqrsPhaseFaultCleanup.RunAsync(Root, RootOwned, Controls, Wave, WaveStartupAttempted,
            Caller, Administrator, Discovery.Count == 0 ? null : Discovery, null, ScenarioDeadline, SdkCall, McpCall,
            Arms.FirstOrDefault(), failures, Arms.Skip(1).ToArray());
        foreach (var cancellation in CallCancellations)
        { ServerFailureObserver.Observe(cancellation.Dispose, failures); }
    }
}
