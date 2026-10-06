using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaCheckpointProtocolGateNode : IAsyncDisposable
{
    internal const long SnapshotCut = 3;
    internal const long TailIndex = SnapshotCut + 1;
    internal const long Term = 1;
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica";
    private const string VoterA = "checkpoint-voter-a";
    private const string VoterB = "checkpoint-voter-b";
    private const string VoterC = "checkpoint-voter-c";
    private readonly ReplicaMaterializerLifecycleStores stores;
    private bool disposed;

    internal ReplicaCheckpointProtocolGateNode(string directory, Guid incarnation, Action<CommitStage, long, int>? observer = null)
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], directory, incarnation);
        stores = ReplicaMaterializerLifecycleStores.Open(
            () => new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = incarnation, FaultObserver = observer }),
            () => new(new(Path.Combine(directory, ReplicaDirectory)) { Incarnation = incarnation }));
        DurableReplicaLog? openedLog = null;
        try
        {
            Log = openedLog = new(stores.Replica, RecoveryExecutionOptions.Configuration(Configuration));
            Database = new(stores.Canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.TimeSeriesExecution());
            Snapshots = new(stores.Canonical, Log, RecoveryExecutionOptions.Configuration(Configuration), RecoveryExecutionOptions.Replica());
            Materializer = new(Database, Log, Snapshots, RecoveryExecutionOptions.Replica());
        }
        catch (Exception error)
        {
            List<Exception> failures = [error];
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedLog?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(stores.Dispose, failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
            throw;
        }
    }

    internal ReplicaConfiguration Configuration { get; }
    internal ZoneTreeStore Canonical => stores.Canonical;
    internal DatabaseEngine Database { get; }
    internal DurableReplicaLog Log { get; }
    internal ReplicaSnapshotStore Snapshots { get; }
    internal ReplicaMaterializer Materializer { get; }
    internal string SnapshotDirectory => Path.Combine(Configuration.Directory, ReplicaProtocol.SnapshotDirectory);

    internal async Task SeedAsync(long applied, CancellationToken cancellationToken)
    {
        Log.SaveTermAndVote(Term, Configuration.LocalId);
        Log.Append([new(1, Term, null), new(2, Term, null), new(SnapshotCut, Term, null), new(TailIndex, Term, null)]);
        Materializer.Commit(applied);
        await Materializer.WaitForApplyAsync(applied, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        List<Exception> failures = [];
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Materializer.DisposeAsync().AsTask(), failures);
        ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
        try
        {
            try
            { stores.Dispose(); }
            catch (Exception original)
            { throw new AggregateException(original); }
        }
        catch (AggregateException wrapper)
        { ReplicaMaterializerLifecycleErrors.AddWrapped(wrapper, failures); }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }
}
