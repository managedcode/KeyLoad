using System.Collections.Concurrent;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed class RequestCqrsCohortCheckpointScenario(bool failNativeFlush, CancellationToken token)
{
    internal const string NativeFailure = "Original native checkpoint SnapshotFlushed fault.";
    private const long InitialCut = 1;
    private const long FollowingCut = 2;
    private const long InitialTerm = 1;
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int armed;
    private readonly ConcurrentDictionary<string, (ReplicaSnapshot Snapshot, byte[] Entries)> prepared =
        new(StringComparer.Ordinal);

    internal Task Entered => entered.Task;
    internal IReadOnlyDictionary<string, (ReplicaSnapshot Snapshot, byte[] Entries)> Prepared => prepared;
    internal void Release() => released.TrySetResult();

    internal Action<CommitStage, long, int>? Observer(string voter)
        => voter == RequestCqrsCohortScenario.LocalVoter ? Observe : null;

    internal void Prepare(string voter, ReplicaLifecycleFixture journal)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsCohortRuntimeFixture.StartupBound, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        journal.Log.SaveTermAndVote(InitialTerm, voter);
        journal.Log.Append([new(InitialCut, InitialTerm, null), new(FollowingCut, InitialTerm, null)]);
        journal.Materializer.Commit(InitialCut);
        journal.Materializer.WaitForApplyAsync(InitialCut, linked.Token).GetAwaiter().GetResult();
        var snapshot = journal.Materializer.Snapshots.Create(InitialCut, InitialTerm);
        var verified = journal.Database.Store.VerifySnapshot(Path.Combine(journal.Configuration.Directory,
            ReplicaProtocol.SnapshotDirectory, snapshot.FileName));
        if (verified.AppliedPosition != InitialCut || verified.Incarnation != journal.Configuration.Incarnation)
        { throw new InvalidOperationException("The genuine initial checkpoint fingerprint is invalid."); }
        journal.Materializer.Commit(FollowingCut);
        journal.Materializer.WaitForApplyAsync(FollowingCut, linked.Token).GetAwaiter().GetResult();
        var entries = journal.Log.Read(FollowingCut, journal.Configuration.MaxAppendEntries, journal.Configuration.MaxAppendBytes);
        if (!prepared.TryAdd(voter, (snapshot, global::KeyLoad.NativeSerialization.Serialize(entries))))
        { throw new InvalidOperationException("The native checkpoint owner was prepared twice."); }
        if (voter == RequestCqrsCohortScenario.LocalVoter)
        { Volatile.Write(ref armed, 1); }
    }

    private void Observe(CommitStage stage, long _, int _1)
    {
        if (stage != CommitStage.SnapshotFlushed || Interlocked.Exchange(ref armed, 0) == 0)
        { return; }
        entered.TrySetResult();
        released.Task.GetAwaiter().GetResult();
        if (failNativeFlush)
        { throw new IOException(NativeFailure); }
    }
}
