using System.Security.Cryptography;
using KeyLoad.FunctionalTests.Authorization;
using KeyLoad.FunctionalTests.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.FunctionalTests.DatabaseComposition;

/// <summary>Restores a backup of the original execution source into a distinct, same-group physical store.</summary>
internal sealed class DatabaseCompositionReplicaCut : IDisposable
{
    private const string TargetSuffix = "-composition-replica-cut";
    private const string BackupSuffix = "-composition-replica-backup";
    private const string RootPrincipal = "root";
    private const string ReplicaCutMismatch = "The execution replica image differs from its original identity and source cut.";
    private const int NoCleanupFailures = 0;
    private const long RestoreCommits = 1;
    private const bool DeleteOwnedTreeRecursively = true;
    private readonly TestDatabase source;
    private readonly string backup;
    private readonly string directory;
    private readonly TestDatabaseCloseState close = new();
    internal ZoneTreeStore Store
    {
        get;
    }
    internal DatabaseEngine Database
    {
        get;
    }

    private DatabaseCompositionReplicaCut(TestDatabase source, ZoneTreeStore store, string directory, string backup)
    {
        this.source = source;
        this.directory = directory;
        this.backup = backup;
        Store = store;
        Database = source.CreateConfiguredDatabase(store);
    }

    internal static async Task RunAsync(Func<TestDatabase> seed,
        Func<TestDatabase, DatabaseCompositionReplicaCut, Task> body)
    {
        TestDatabase? source = null;
        DatabaseCompositionReplicaCut? target = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            source = seed();
            target = await CreateAsync(source);
            await body(source, target);
        }, failures);
        if (target is not null)
        {
            ServerFailureObserver.Observe(target.Dispose, failures);
        }
        if (source is not null)
        {
            ServerFailureObserver.Observe(source.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<DatabaseCompositionReplicaCut> CreateAsync(TestDatabase source)
    {
        var backup = source.Directory + BackupSuffix;
        var directory = source.Directory + TargetSuffix;
        ZoneTreeStore? target = null;
        try
        {
            var captured = await source.Store.CreateBackupAsync(backup);
            await ZoneTreeStore.RestoreAsync(backup, directory, source.StorageExecution,
                source.Store.Identity.Incarnation, source.Store.Identity.SigningKey.ToArray());
            target = new(new(directory), source.StorageExecution);
            if (captured != source.Store.Position
                || target.Identity.NodeId == source.Store.Identity.NodeId
                || target.Identity.Incarnation != source.Store.Identity.Incarnation
                || !CryptographicOperations.FixedTimeEquals(target.Identity.SigningKey.Span, source.Store.Identity.SigningKey.Span)
                || target.Position != source.Store.Position + RestoreCommits
                || !ReplicatedImage(target).SequenceEqual(ReplicatedImage(source.Store), StringComparer.Ordinal))
            {
                throw new InvalidOperationException(ReplicaCutMismatch);
            }
            return new(source, target, directory, backup);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            var joined = target is null;
            if (target is not null)
            {
                var cleanup = new List<Exception>();
                ServerFailureObserver.Observe(target.Dispose, cleanup);
                joined = cleanup.Count == NoCleanupFailures;
                failures.AddRange(cleanup);
            }
            if (joined)
            {
                ServerFailureObserver.Observe(() => Delete(directory, backup), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal CommitReceipt Commit(params Mutation[] mutations)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var request = new CommandRequest(Guid.NewGuid(), source.Partition, [.. mutations]);
        var work = new ReadExecutionBudget(Database.EvaluationClock, token);
        var original = Database.CreateCommand(OperationKind.Batch, request.CommandId, RootPrincipal,
            Database.EvaluationClock.GetUtcNow(), request, work);
        return Database.Apply(original).Get<CommitReceipt>();
    }

    public void Dispose()
    {
        lock (close.Gate)
        {
            if (close.TryBeginJoin())
            {
                var failures = new List<Exception>();
                ServerFailureObserver.Observe(Store.Dispose, failures);
                close.CompleteJoin(failures);
            }
        }
        close.Delete(() => Delete(directory, backup));
    }

    /// <summary>Returns canonical replicated records, excluding the restore pause and physical owner's local position.</summary>
    internal static string[] ReplicatedImage(ZoneTreeStore store)
    {
        var paused = Convert.ToHexString(StoreFileFixture.DispatchPausedKey) + ":";
        var position = Convert.ToHexString(NativeStorePositionOracle.Key) + ":";
        return TeamRowPolicyAssertions.Image(store).Where(row => !row.StartsWith(paused, StringComparison.Ordinal)
            && !row.StartsWith(position, StringComparison.Ordinal)).ToArray();
    }

    private static void Delete(string directory, string backup)
    {
        var failures = new List<Exception>();
        foreach (var owned in new[] { directory, backup })
        {
            ServerFailureObserver.Observe(() =>
            {
                if (Directory.Exists(owned))
                {
                    Directory.Delete(owned, DeleteOwnedTreeRecursively);
                }
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
