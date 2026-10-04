using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaMaterializerLifecycleFixture : IAsyncDisposable, IDisposable
{
    private const string DirectoryPrefix = "keyload-materializer-lifecycle-";
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica";
    private const string VoterA = "materializer-voter-a";
    private const string VoterB = "materializer-voter-b";
    private const string VoterC = "materializer-voter-c";
    private const string ObserverFailure = "The real materializer journal observer failed after durable flush.";
    private const int Disarmed = 0;
    private const int PauseMode = 1;
    private const int FailureMode = 2;
    internal const long AppliedCut = 1;
    private readonly string directory = ReplicaFixturePaths.NewDirectory(DirectoryPrefix);
    private readonly ReplicaMaterializerLifecycleStores storeOwners;
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int armed;
    private bool ownersClosed;
    private List<Exception>? scenarioFailures;
    private bool cleanupCompleted;

    internal ReplicaMaterializerLifecycleFixture()
    {
        Configuration = new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid());
        Configuration.Validate();
        DurableReplicaLog? openedLog = null;
        ReplicaMaterializerLifecycleStores? openedStores = null;
        try
        {
            openedStores = ReplicaMaterializerLifecycleStores.Open(OpenCanonicalStore, OpenReplicaStore);
            storeOwners = openedStores;
            openedLog = new(storeOwners.Replica, Configuration);
            Log = openedLog;
            Database = new(storeOwners.Canonical, new AuthorizationPolicy());
            Materializer = new(Database, Log, new ReplicaSnapshotStore(storeOwners.Canonical, Log, Configuration));
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedLog?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(() => openedStores?.Dispose(), failures);
            ReplicaMaterializerLifecycleErrors.Attempt(DeleteDirectory, failures);
            throw new AggregateException(failures);
        }
    }

    internal ReplicaConfiguration Configuration { get; }
    internal DurableReplicaLog Log { get; }
    internal DatabaseEngine Database { get; }
    internal ReplicaMaterializer Materializer { get; }
    internal Task Entered => entered.Task;
    private string CanonicalPath => Path.Combine(directory, CanonicalDirectory);
    private string ReplicaPath => Path.Combine(directory, ReplicaDirectory);

    internal void PauseCommittedApply() => ArmCommittedApply(PauseMode);
    internal void FailCommittedApply() => ArmCommittedApply(FailureMode);

    private void ArmCommittedApply(int mode)
    {
        Log.SaveTermAndVote(AppliedCut, VoterA);
        Log.Append([new(AppliedCut, AppliedCut, null)]);
        Volatile.Write(ref armed, mode);
        Materializer.Commit(AppliedCut);
    }

    private void Observe(CommitStage stage, long position, int mutation)
    {
        if (stage != CommitStage.JournalFlushed)
        {
            return;
        }
        var mode = Interlocked.Exchange(ref armed, Disarmed);
        if (mode == Disarmed)
        {
            return;
        }
        entered.TrySetResult();
        if (mode == FailureMode)
        {
            throw new InvalidOperationException(ObserverFailure);
        }
        released.Task.GetAwaiter().GetResult();
    }

    internal void Release() => released.TrySetResult();

    internal async Task RunAsync(Func<Task> scenario)
    {
        var failures = new List<Exception>();
        scenarioFailures = failures;
        try
        {
            await ReplicaMaterializerLifecycleErrors.AttemptAsync(scenario, failures);
        }
        finally
        {
            Release();
            await DisposeAsync();
        }
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    public async ValueTask DisposeAsync()
    {
        if (cleanupCompleted)
        {
            return;
        }
        var failures = scenarioFailures ??= [];
        Release();
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => Materializer.DisposeAsync().AsTask(), failures);
        if (!ownersClosed)
        {
            ownersClosed = true;
            ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
            try
            { storeOwners.Dispose(); }
            catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
            catch (IOException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        }
        ReplicaMaterializerLifecycleErrors.Attempt(DeleteDirectory, failures);
        cleanupCompleted = true;
    }

    public void Dispose()
    {
        if (cleanupCompleted)
        {
            return;
        }
        var failures = scenarioFailures ??= [];
        Release();
        try
        {
            Materializer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (IOException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (InvalidOperationException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (OperationCanceledException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (TimeoutException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        catch (KeyLoadException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        if (!ownersClosed)
        {
            ownersClosed = true;
            ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
            try
            { storeOwners.Dispose(); }
            catch (AggregateException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
            catch (IOException error) { ReplicaMaterializerLifecycleErrors.Add(error, failures); }
        }
        ReplicaMaterializerLifecycleErrors.Attempt(DeleteDirectory, failures);
        cleanupCompleted = true;
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private void DeleteDirectory()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }

    private ZoneTreeStore OpenCanonicalStore() => new(new(CanonicalPath)
    { Incarnation = Configuration.Incarnation, FaultObserver = Observe });

    private ZoneTreeStore OpenReplicaStore() => new(new(ReplicaPath) { Incarnation = Configuration.Incarnation });

    internal async Task AssertReopenedAsync(long cut)
    {
        var identity = storeOwners.Canonical.Identity;
        var failures = new List<Exception>();
        CloseOwners(failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
        using var reopenedCanonical = new ZoneTreeStore(new(CanonicalPath) { Incarnation = Configuration.Incarnation });
        using var reopenedReplica = new ZoneTreeStore(new(ReplicaPath) { Incarnation = Configuration.Incarnation });
        using var reopenedLog = new DurableReplicaLog(reopenedReplica, Configuration);
        var database = new DatabaseEngine(reopenedCanonical, new AuthorizationPolicy());
        await Assert.That(database.LastApplied).IsEqualTo(cut);
        await Assert.That(reopenedLog.State.CommittedIndex).IsEqualTo(cut);
        await Assert.That(reopenedLog.State.LastIndex).IsEqualTo(cut);
        await Assert.That(reopenedCanonical.Identity.NodeId).IsEqualTo(identity.NodeId);
        await Assert.That(reopenedCanonical.Identity.Incarnation).IsEqualTo(identity.Incarnation);
        await Assert.That(reopenedCanonical.Identity.ReadGeneration).IsEqualTo(identity.ReadGeneration);
    }

    private void CloseOwners(List<Exception> failures)
    {
        if (ownersClosed)
        {
            return;
        }
        ownersClosed = true;
        ReplicaMaterializerLifecycleErrors.Attempt(Log.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Attempt(storeOwners.Dispose, failures);
    }

}
