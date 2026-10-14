using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.Orleans;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal sealed class LiveReadCancellationR49Owner
{
    internal string Root = string.Empty;
    internal string DataRoot = string.Empty;
    internal bool RootOwned;
    internal bool WaveStartupAttempted;
    internal LocalRf3ImageTestSession? ImageSession;
    internal RequestCqrsProbeFixture Controls = null!;
    internal RequestCqrsRf3Wave Wave = null!;
    internal RequestCqrsRf3Callers Administrator = null!;
    internal RequestCqrsRf3Callers Caller = null!;
    internal RequestCqrsPhaseFaultIdentity Identity = null!;
    internal IReadOnlyList<ReplicaSiloDiscovery> Discovery = [];
    internal readonly RequestCqrsLifecycleEvidence Lifecycle = new();
    internal readonly List<Guid> Arms = [];
    internal readonly List<CancellationTokenSource> Cancellations = [];
    internal Task<ManagedCode.Communication.Result<LiveQueryPage>>? SdkCall;
    internal Task<RequestCqrsFaultMcpObservation>? McpCall;
    internal AstQueryRequest Query = null!;
    internal AtomicPartitionPlacementResolution Placement = null!;
    internal OutboxStatus Outbox = null!;
    internal CommandRequest Seed = null!;
    internal CommitReceipt SeedReceipt = null!;
    internal CommandRequest Update = null!;
    internal CommitReceipt? Updated;
    internal CommitReceipt CurrentReceipt => Updated ?? SeedReceipt;
    internal long Revision => Updated is null ? LiveReadCancellationR49Protocol.InitialRevision : LiveReadCancellationR49Protocol.CurrentRevision;
    internal string Json => Updated is null ? LiveReadCancellationR49Protocol.InitialJson : LiveReadCancellationR49Protocol.CurrentJson;
    internal ReadLiveQueryRequest Original = null!;
    internal Func<CancellationToken, Task> Restart = null!;
    internal EntityRef Reference => new(Identity.Partition, LiveReadCancellationR49Protocol.Collection,
        LiveReadCancellationR49Protocol.Document);

    internal static async Task RunAsync(CancellationToken token)
    {
        var owner = new LiveReadCancellationR49Owner();
        var failures = new List<Exception>();
        owner.Lifecycle.SetTokens(token, token, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await LiveReadCancellationR49Setup.StartAsync(owner, token);
            await LiveReadCancellationR49Flow.RunAsync(owner, token);
        }, failures);
        owner.Lifecycle.RecordFirstFailureIfAny(failures);
        await owner.JoinAsync(failures);
        owner.Lifecycle.RecordTerminal();
        owner.Lifecycle.ThrowWithContext(failures);
    }

    private async Task JoinAsync(List<Exception> failures)
    {
        var before = failures.Count;
        foreach (var cancellation in Cancellations)
        { await ServerFailureObserver.ObserveAsync(cancellation.CancelAsync, failures); }
        await RequestCqrsPhaseFaultCleanup.RunAsync(Root, RootOwned && ImageSession is null,
            Controls, Wave, WaveStartupAttempted, Caller, Administrator,
            Discovery.Count == 0 ? null : Discovery, null, null, SdkCall, McpCall,
            Arms.FirstOrDefault(), failures, Arms.Skip(1).ToArray());
        foreach (var cancellation in Cancellations)
        { ServerFailureObserver.Observe(cancellation.Dispose, failures); }
        if (ImageSession is null)
        { return; }
        var cleanup = failures.Skip(before).ToList();
        var prior = cleanup.Count;
        var stopped = (Wave is not null && cleanup.Count == 0) || !WaveStartupAttempted;
        await ClusterFixtureLocalImageLifecycle.JoinAsync(ImageSession, DataRoot, stopped,
            !WaveStartupAttempted, cleanup);
        failures.AddRange(cleanup.Skip(prior));
        if (RootOwned && stopped && cleanup.Count == 0)
        { ServerFailureObserver.Observe(() => Directory.Delete(Root, recursive: true), failures); }
    }
}
