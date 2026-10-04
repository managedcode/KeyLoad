using System.Text;
using KeyLoad.Core;
using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.RecoveryTests;

internal sealed class ReplicaTermMetadataFixture : IDisposable
{
    private const int MaximumSubordinateOwners = 2;
    private const string TooManyOwners = "The replica term fixture only supports its two scoped subordinate owners.";
    internal const string VoterA = "term-node-a";
    internal const string VoterB = "term-node-b";
    internal const string VoterC = "term-node-c";
    internal const string SystemNamespace = "system";
    internal const string AppliedPositionKey = "last-applied";
    internal const string TenantId = "term-tenant";
    internal const string DatabaseId = "term-database";
    internal const string TransactionDomainId = "term-domain";
    internal const string PartitionKey = "term-partition";
    internal const string DocumentCollection = "term-documents";
    internal const string DocumentId = "large-document";
    internal const string LargeDocumentPrefix = "{\"body\":\"";
    internal const string LargeDocumentSuffix = "\"}";
    internal const string TestDirectoryPrefix = "keyload-replica-term-metadata-";
    internal const int LargeDocumentCharacters = 1_000_000;
    internal string DirectoryPath { get; }
    internal ZoneTreeStore Store => lifetime.BorrowReplica();
    internal DurableReplicaLog Log => lifetime.BorrowLog();
    internal DatabaseEngine Database => lifetime.Database;
    private readonly ReplicaTermMetadataLifetime lifetime;
    private readonly List<IDisposable> subordinateOwners = [];
    private bool disposed;

    internal ReplicaTermMetadataFixture(Action<CommitStage, long, int>? faultObserver = null)
    {
        DirectoryPath = ReplicaFixturePaths.NewDirectory(TestDirectoryPrefix);
        lifetime = ReplicaTermMetadataLifetime.Open(DirectoryPath, faultObserver);
    }

    internal ReplicaConfiguration Configuration => lifetime.Configuration;

    internal static async Task RunAsync(Func<ReplicaTermMetadataFixture, Task> scenario,
        Action<CommitStage, long, int>? faultObserver = null)
    {
        using var fixture = new ReplicaTermMetadataFixture(faultObserver);
        List<Exception> failures = [];
        await ReplicaMaterializerLifecycleErrors.AttemptAsync(() => scenario(fixture), failures);
        ReplicaMaterializerLifecycleErrors.Attempt(fixture.Dispose, failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    internal T Own<T>(T resource) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (subordinateOwners.Count == MaximumSubordinateOwners)
        {
            List<Exception> failures = [new InvalidOperationException(TooManyOwners)];
            AttemptOwner(resource, failures);
            ReplicaMaterializerLifecycleErrors.Throw(failures);
        }
        subordinateOwners.Add(resource);
        return resource;
    }

    internal void CloseStore() => lifetime.CloseReplica();

    internal void CloseLog() => lifetime.CloseLog();

    internal static CommandRequest AtomicBatch(string documentJson)
    {
        var commandId = Guid.NewGuid();
        var partition = new PartitionRef(TenantId, DatabaseId, TransactionDomainId, PartitionKey);
        return new(commandId, partition, [new PutDocument(DocumentCollection, DocumentId, documentJson)], OwnershipEpoch: 1);
    }

    internal ReplicatedOperation Operation(CommandRequest batch)
        => Database.NormalizeOperation(new(batch.CommandId, OperationKind.Batch, VoterA, DateTimeOffset.UnixEpoch,
            Encoding.UTF8.GetString(JsonDefaults.Serialize(batch))));

    internal static string LargeDocumentJson()
        => LargeDocumentPrefix + new string('x', LargeDocumentCharacters) + LargeDocumentSuffix;

    internal static void PrepareLog(DurableReplicaLog replicaLog, long entryTerm, long logTerm = 2)
    {
        replicaLog.SaveTermAndVote(logTerm, null);
        replicaLog.Append([new(1, entryTerm, null)]);
        replicaLog.Commit(1);
    }

    internal static void WriteAppliedCut(ZoneTreeStore zoneTreeStore, long cut = 1)
        => zoneTreeStore.Commit((transaction, _) =>
        {
            transaction.PutRecord(KeyCodec.Encode(SystemNamespace, AppliedPositionKey), cut);
            return true;
        });

    internal static ReplicaSnapshot CreateVerifiedSnapshot(ReplicaTermMetadataFixture fixture,
        ZoneTreeStore canonical, ReplicaConfiguration configuration)
    {
        var log = fixture.Own(new DurableReplicaLog(canonical, configuration));
        log.SaveTermAndVote(2, null);
        log.Append([new(1, 1, null), new(2, 2, null)]);
        log.Commit(2);
        WriteAppliedCut(canonical, 2);
        var snapshots = new ReplicaSnapshotStore(canonical, log, configuration);
        return snapshots.Create(2, 2);
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        disposed = true;
        List<Exception> failures = [];
        for (var ownerIndex = subordinateOwners.Count - 1; ownerIndex >= 0; ownerIndex--)
        {
            AttemptOwner(subordinateOwners[ownerIndex], failures);
        }
        try
        { lifetime.Dispose(); }
        catch (AggregateException wrapper)
        { ReplicaMaterializerLifecycleErrors.AddWrapped(wrapper, failures); }
        ReplicaMaterializerLifecycleErrors.Attempt(() => Directory.Delete(DirectoryPath, true), failures);
        ReplicaMaterializerLifecycleErrors.Throw(failures);
    }

    private static void AttemptOwner(IDisposable? owner, List<Exception> failures)
    {
        if (owner is not null)
        {
            ReplicaMaterializerLifecycleErrors.Attempt(() => DisposeOwner(owner, failures), failures);
        }
    }

    private static void DisposeOwner(IDisposable owner, List<Exception> failures)
    {
        try
        {
            owner.Dispose();
        }
        catch (AggregateException cleanup)
        {
            ReplicaMaterializerLifecycleErrors.Add(cleanup, failures);
        }
    }
}
