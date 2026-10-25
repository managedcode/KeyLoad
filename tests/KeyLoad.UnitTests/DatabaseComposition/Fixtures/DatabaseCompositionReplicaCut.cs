using System.Security.Cryptography;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Authorization;

namespace KeyLoad.UnitTests.DatabaseComposition;

/// <summary>Installs the original execution source image into a distinct, same-group physical store.</summary>
internal sealed class DatabaseCompositionReplicaCut : IDisposable
{
    private const string TargetSuffix = "-composition-replica-cut";
    private const string SnapshotName = "composition-replica-cut.snapshot";
    private const string RootPrincipal = "root";
    private const string ReplicaCutMismatch = "The execution replica image differs from its original identity and source cut.";
    private const int NoCleanupFailures = 0;
    private const bool DeleteOwnedTreeRecursively = true;
    private readonly TestDatabase source;
    private readonly string snapshot;
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

    private DatabaseCompositionReplicaCut(TestDatabase source, ZoneTreeStore store, string directory, string snapshot)
    {
        this.source = source;
        this.directory = directory;
        this.snapshot = snapshot;
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
            target = Create(source);
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

    private static DatabaseCompositionReplicaCut Create(TestDatabase source)
    {
        var snapshot = Path.Combine(source.Directory, SnapshotName);
        var directory = source.Directory + TargetSuffix;
        ZoneTreeStore? target = null;
        try
        {
            var captured = source.Store.CreateSnapshot(snapshot, source.Database.LastApplied);
            target = new(new(directory)
            {
                Incarnation = source.Store.Identity.Incarnation,
                SigningKey = source.Store.Identity.SigningKey
            }, source.StorageExecution, UnitExecutionOptions.PointCacheExecution());
            var installed = target.InstallSnapshot(snapshot, source.Database.LastApplied);
            if (installed != captured || captured.Incarnation != source.Store.Identity.Incarnation
                || captured.AppliedPosition != source.Database.LastApplied
                || target.Identity.NodeId == source.Store.Identity.NodeId
                || target.Identity.Incarnation != source.Store.Identity.Incarnation
                || !CryptographicOperations.FixedTimeEquals(target.Identity.SigningKey.Span, source.Store.Identity.SigningKey.Span)
                || target.Position != source.Store.Position
                || !TeamRowPolicyAssertions.Image(target).SequenceEqual(TeamRowPolicyAssertions.Image(source.Store), StringComparer.Ordinal))
            {
                throw new InvalidOperationException(ReplicaCutMismatch);
            }
            return new(source, target, directory, snapshot);
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
                ServerFailureObserver.Observe(() => Delete(directory, snapshot), failures);
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
            Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request), work);
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
        close.Delete(() => Delete(directory, snapshot));
    }

    private static void Delete(string directory, string snapshot)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, DeleteOwnedTreeRecursively);
            }
        }, failures);
        ServerFailureObserver.Observe(() => File.Delete(snapshot), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
