using System.Security.Cryptography;
using KeyLoad.CrashHost;
using KeyLoad.Replication;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaCheckpointProtocolGateFixture : IAsyncDisposable
{
    private const string DirectoryPrefix = "keyload-checkpoint-protocol-";
    private const string TargetDirectory = "target";
    private const string SourceDirectory = "source";
    private const string PlanningNotStarted = "Protocol planning scope has not started.";
    private static readonly TimeSpan ImagePollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan PlanningTimeout = TimeSpan.FromSeconds(15);
    private readonly string directory = ReplicaFixturePaths.NewDirectory(DirectoryPrefix);
    private readonly ReplicaCheckpointProtocolGatePause pause = new();
    private readonly List<Exception> failures = [];
    private ReplicaCheckpointProtocolGateNode? source;
    private ReplicaSnapshot? incoming;
    private Task<ReplicaSnapshot?>? publication;
    private ReplicaCheckpointProtocolPlanningScope? planning;
    private Task? cleanup;
    private bool runStarted;

    internal ReplicaCheckpointProtocolGateFixture()
    {
        try
        {
            Node = new(Path.Combine(directory, TargetDirectory), Guid.NewGuid(), pause.Observe);
        }
        catch (Exception error)
        {
            failures.Add(error);
            ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(directory, true), failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal ReplicaCheckpointProtocolGateNode Node { get; }
    internal Task NativeFlushed => pause.Entered;
    internal Task<ReplicaSnapshot?> Publication => publication ?? throw new InvalidOperationException("Checkpoint publication has not started.");
    internal void ReleaseNativeFlush() => pause.Release();

    internal void StartPlanning(CancellationToken cancellationToken)
        => planning = new(Node, PlanningTimeout, cancellationToken);

    internal Task<ReplicaCheckpointProtocolPlanningObservation> WaitForPlanningAsync(CancellationToken cancellationToken)
        => RequiredPlanning.WaitForEntryAsync(cancellationToken);

    internal void ReleasePlanning() => RequiredPlanning.Release();

    internal Task<ReplicaCheckpointProtocolPlanningObservation> JoinPlanningAsync()
        => RequiredPlanning.JoinAsync();

    private ReplicaCheckpointProtocolPlanningScope RequiredPlanning
        => planning ?? throw new InvalidOperationException(PlanningNotStarted);

    internal async Task PrepareAsync(bool receive, CancellationToken cancellationToken)
    {
        await Node.SeedAsync(receive ? 1 : ReplicaCheckpointProtocolGateNode.SnapshotCut, cancellationToken);
        if (!receive)
        {
            return;
        }
        source = new(Path.Combine(directory, SourceDirectory), Node.Configuration.Incarnation);
        await source.SeedAsync(ReplicaCheckpointProtocolGateNode.SnapshotCut, cancellationToken);
        incoming = (await source.Materializer.CreateCheckpointAsync(cancellationToken))!;
        var offset = Node.Snapshots.Begin(incoming);
        while (offset < incoming.Length)
        {
            var bytes = source.Snapshots.ReadChunk(incoming.TransferId, offset, source.Configuration.SnapshotChunkBytes);
            offset = Node.Snapshots.Append(incoming.TransferId, offset, bytes);
        }
    }

    internal void StartPublication(bool receive, CancellationToken cancellationToken)
    {
        pause.Arm();
        publication = receive ? InstallAsync(cancellationToken) : Node.Materializer.CreateCheckpointAsync(cancellationToken);
    }

    private async Task<ReplicaSnapshot?> InstallAsync(CancellationToken cancellationToken)
        => await Node.Materializer.InstallCheckpointAsync(incoming!.TransferId, cancellationToken);

    internal async Task<string> WaitForPublishedImageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = Directory.EnumerateFiles(Node.SnapshotDirectory, "*" + ReplicaProtocol.SnapshotExtension)
                .SingleOrDefault(path => Path.GetFileName(path) != ReplicaProtocol.IncomingImage);
            if (image is not null)
            {
                return image;
            }
            if (Publication.IsCompleted)
            {
                _ = await Publication;
                throw new InvalidOperationException("Checkpoint publication completed without its verified image.");
            }
            await Task.Delay(ImagePollInterval, TimeProvider.System, cancellationToken);
        }
    }

    internal async Task AssertPublishedAsync(ReplicaSnapshot snapshot, CancellationToken cancellationToken)
    {
        await Assert.That(snapshot.Index).IsEqualTo(ReplicaCheckpointProtocolGateNode.SnapshotCut);
        await Assert.That(snapshot.Term).IsEqualTo(ReplicaCheckpointProtocolGateNode.Term);
        await Assert.That(Node.Snapshots.Current).IsEqualTo(snapshot);
        var state = Node.Log.State;
        await Assert.That(state.CommittedIndex).IsEqualTo(snapshot.Index);
        await Assert.That(state.LastIndex).IsEqualTo(ReplicaCheckpointProtocolGateNode.TailIndex);
        await Assert.That(state.Term).IsEqualTo(ReplicaCheckpointProtocolGateNode.Term);
        await Assert.That(state.VotedFor).IsEqualTo(Node.Configuration.LocalId);
        await Assert.That(Node.Log.TermAt(snapshot.Index)).IsEqualTo(snapshot.Term);
        var tail = Node.Log.Read(ReplicaCheckpointProtocolGateNode.TailIndex, Node.Configuration.MaxAppendEntries, Node.Configuration.MaxAppendBytes);
        await Assert.That(tail.Length).IsEqualTo(1);
        await Assert.That(tail[0]).IsEqualTo(new ReplicaEntry(ReplicaCheckpointProtocolGateNode.TailIndex, ReplicaCheckpointProtocolGateNode.Term, null));
        var path = Path.Combine(Node.SnapshotDirectory, snapshot.FileName);
        await Assert.That(new FileInfo(path).Length).IsEqualTo(snapshot.Length);
        using var image = File.OpenRead(path);
        await Assert.That(Convert.ToHexStringLower(await SHA256.HashDataAsync(image, cancellationToken))).IsEqualTo(snapshot.Sha256);
        var verified = Node.Canonical.VerifySnapshot(path);
        await Assert.That(verified.AppliedPosition).IsEqualTo(snapshot.Index);
        await Assert.That(verified.Incarnation).IsEqualTo(Node.Configuration.Incarnation);
        await Assert.That(Node.Database.LastApplied).IsEqualTo(snapshot.Index);
    }

    internal async Task RunAsync(Func<Task> scenario)
    {
        runStarted = true;
        try
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(scenario, failures);
        }
        finally { await DisposeAsync(); }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    public async ValueTask DisposeAsync()
    {
        cleanup ??= CleanupAsync();
        await cleanup;
        if (!runStarted)
        {
            ReplicaMaterializerLifecycleErrors.Throw(failures);
        }
    }

    private async Task CleanupAsync()
    {
        pause.Release();
        var activePlanning = planning;
        if (activePlanning is not null)
        {
            activePlanning.Release();
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(async () => { _ = await activePlanning.JoinAsync(); }, failures);
        }
        if (publication is not null)
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(async () => { _ = await publication; }, failures);
        }
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Node.DisposeAsync().AsTask(), failures);
        if (source is not null)
        {
            try
            {
                try
                { await source.DisposeAsync(); }
                catch (Exception original)
                { throw new AggregateException(original); }
            }
            catch (AggregateException wrapper)
            { ReplicaMaterializerLifecycleErrors.AddWrapped(wrapper, failures); }
        }
        ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(directory, true), failures);
    }
}
