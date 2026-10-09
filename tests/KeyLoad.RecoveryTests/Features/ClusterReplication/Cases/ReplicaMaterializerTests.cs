using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Security;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

/// <summary>AC-REP-002/004: committed log cuts control real canonical materialization.</summary>
internal sealed class ReplicaMaterializerTests
{
    private const string VoterA = "a";
    private const string VoterB = "b";
    private const string VoterC = "c";
    private const string CanonicalDirectory = "canonical";
    private const string LogDirectory = "log";
    private const string TestDirectoryPrefix = "keyload-materializer-";

    /// <summary>AC-REP-002: an uncommitted durable entry never changes the canonical apply cut.</summary>
    [Test]
    public async Task UncommittedEntryRemainsInvisibleUntilDurableCommit()
    {
        await using var fixture = new Fixture();
        fixture.Log.SaveTermAndVote(1, VoterA);
        fixture.Log.Append([new(1, 1, null), new(2, 1, null)]);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(0);
        fixture.Materializer.Commit(1);
        await fixture.Materializer.WaitForApplyAsync(1, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(1);
        await Assert.That(fixture.Log.State.LastIndex).IsEqualTo(2);
        await Assert.That(fixture.Log.State.CommittedIndex).IsEqualTo(1);
    }

    /// <summary>AC-REP-004: canonical snapshot metadata uses the exact materialized committed cut.</summary>
    [Test]
    public async Task CheckpointContainsTheCommittedCutAndPreservesUncommittedTail()
    {
        await using var fixture = new Fixture();
        fixture.Log.SaveTermAndVote(1, VoterA);
        fixture.Log.Append([new(1, 1, null), new(2, 1, null), new(3, 1, null)]);
        fixture.Materializer.Commit(2);
        await fixture.Materializer.WaitForApplyAsync(2, TestContext.Current!.Execution.CancellationToken);
        var image = await fixture.Materializer.CreateCheckpointAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(image).IsNotNull();
        await Assert.That(image!.Index).IsEqualTo(2);
        await Assert.That(fixture.Log.State.LastIndex).IsEqualTo(3);
        await Assert.That(fixture.Log.ReadEntry(3)).IsNotNull();
        await Assert.That(fixture.Database.Store.VerifySnapshot(Path.Combine(fixture.Configuration.Directory,
            ReplicaProtocol.SnapshotDirectory, image.FileName)).AppliedPosition).IsEqualTo(2);
    }

    /// <summary>AC-REP-002: readiness rejects canonical progress without committed log evidence.</summary>
    [Test]
    public async Task CanonicalCutAheadOfCommittedLogFailsClosedOnRecovery()
    {
        await using var fixture = new Fixture();
        var appliedKey = KeySpace.Applied.ToArray();
        fixture.Database.Store.Commit((transaction, _) => { transaction.PutRecord(appliedKey, 1L); return true; });
        var exception = Assert.ThrowsExactly<KeyLoadException>(fixture.Materializer.Recover);
        await Assert.That(exception.Code).IsEqualTo(ErrorCode.RecoveryRequired);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string directory = KeyLoad.CrashHost.ReplicaFixturePaths.NewDirectory(TestDirectoryPrefix);
        private readonly ZoneTreeStore canonical;
        private readonly ZoneTreeStore replica;
        public ReplicaConfiguration Configuration { get; }
        public DatabaseEngine Database { get; }
        public DurableReplicaLog Log { get; }
        public ReplicaMaterializer Materializer { get; }

        public Fixture()
        {
            Configuration = new(VoterA, [VoterA, VoterB, VoterC], directory, Guid.NewGuid());
            canonical = new(new(Path.Combine(directory, CanonicalDirectory)) { Incarnation = Configuration.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            replica = new(new(Path.Combine(directory, LogDirectory)) { Incarnation = Configuration.Incarnation }, RecoveryExecutionOptions.StorageExecution(), RecoveryExecutionOptions.PointCacheExecution());
            Log = new(replica, RecoveryExecutionOptions.Configuration(Configuration));
            Database = new(canonical, new AuthorizationPolicy(), RecoveryExecutionOptions.DatabaseLimits(), RecoveryExecutionOptions.DueWork(), RecoveryExecutionOptions.EventSource(), RecoveryExecutionOptions.Messaging(), RecoveryExecutionOptions.GraphExecution(), RecoveryExecutionOptions.ChangeFeedExecution(), RecoveryExecutionOptions.BlobExecution(), RecoveryExecutionOptions.NativeClaimsExecution(), RecoveryExecutionOptions.TimeSeriesExecution(), RecoveryExecutionOptions.MovementCheckpoints(), KeyLoad.Core.UnavailablePartitionMovementCheckpointVerifier.Instance);
            Materializer = new(Database, Log, new ReplicaSnapshotStore(canonical, Log, RecoveryExecutionOptions.Configuration(Configuration), RecoveryExecutionOptions.Replica()), RecoveryExecutionOptions.Replica());
        }

        public async ValueTask DisposeAsync()
        {
            await Materializer.DisposeAsync();
            Log.Dispose();
            replica.Dispose();
            canonical.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
