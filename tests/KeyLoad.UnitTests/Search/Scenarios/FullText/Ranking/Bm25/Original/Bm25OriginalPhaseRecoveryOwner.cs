using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class Bm25OriginalPhaseRecoveryOwner
{
    private const string DirectoryPrefix = "keyload-bm25-phase-recovery-";
    private const string MissingOwner = "The original BM25 native owner is not open.";
    private readonly StoreIdentity originalIdentity;
    private bool originalJoined;
    private Guid originalReplicaId;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    private TestDatabaseReplicaAdmission? coldAdmission;
    internal TestDatabase Source
    {
        get;
    }
    private string ReplicaDirectory => Source.Directory + TestDatabaseReplicaDirectory.Suffix;

    private Bm25OriginalPhaseRecoveryOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Source = new(directory: directory, nativeReplicaAdmission: true,
            nativeReplicaDirectory: directory + TestDatabaseReplicaDirectory.Suffix);
        originalIdentity = Source.Store.Identity;
    }

    internal ZoneTreeStore Store => originalJoined
        ? coldStore ?? throw new InvalidOperationException(MissingOwner) : Source.Store;
    internal DatabaseEngine Database => originalJoined
        ? coldDatabase ?? throw new InvalidOperationException(MissingOwner) : Source.Database;

    internal static async Task RunAsync(Func<Bm25OriginalPhaseRecoveryFlow, Task> operation)
    {
        var failures = new List<Exception>();
        Bm25OriginalPhaseRecoveryOwner? owner = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner = new();
            var flow = new Bm25OriginalPhaseRecoveryFlow(owner);
            flow.Seed();
            await operation(flow);
        }, failures);
        if (owner is not null)
        {
            ServerFailureObserver.Observe(owner.Join, failures);
            if (failures.Count == 0)
            {
                ServerFailureObserver.Observe(owner.DeleteJoinedDirectories, failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal OperationResult Submit(ReplicatedOperation operation)
        => originalJoined
            ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).Submit(operation, explicitTime: true)
            : Source.SubmitIssued(operation);

    internal ReplicaEntry Entry(Guid commandId) => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).ReadRetainedEntry(commandId)
        : Source.ReadRetainedEntry(commandId);

    internal (long LastIndex, long CommittedIndex, long Term) JournalCut() => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).ReadJournalCut()
        : Source.ReadJournalCut();

    internal void Reopen(DatabaseLimits? configuredLimits = null)
    {
        if (configuredLimits is not null)
        {
            configuredLimits.Validate();
            var original = Source.Database.Limits;
            if (configuredLimits.MaxScanRecords > original.MaxScanRecords
                || configuredLimits != (original with
                {
                    MaxScanRecords = configuredLimits.MaxScanRecords
                }))
            {
                throw new ArgumentException("Only the original fixture's validated lower record limit can change.", nameof(configuredLimits));
            }
        }
        Join();
        var replica = ReadReplicaIdentity();
        if (originalReplicaId == Guid.Empty || replica.NodeId != originalReplicaId)
        {
            throw new InvalidOperationException("The cold replica is not its original owned native identity.");
        }
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = originalIdentity.Incarnation
        }, originalIdentity.NodeId, Source.StorageExecution);
        coldDatabase = CreateColdDatabase(coldStore, configuredLimits);
        coldAdmission = Source.CreateTargetAdmission(coldDatabase, coldStore,
            Source.Directory, ReplicaDirectory, replica.NodeId);
    }

    private DatabaseEngine CreateColdDatabase(ZoneTreeStore store, DatabaseLimits? configuredLimits)
    {
        if (configuredLimits is null)
        {
            return Source.CreateConfiguredDatabase(store);
        }
        var owner = store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)?.DefaultShard)
            ?? throw new InvalidOperationException("The original committed native catalog owner is missing.");
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(configuredLimits),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), null, Source.Database.EvaluationClock);
        return create(store, owner);
    }

    internal void BindReplicaIdentity() => originalReplicaId = ReadReplicaIdentity().NodeId;

    private StoreIdentity ReadReplicaIdentity()
    {
        var limits = Source.StorageExecution.Value;
        var replica = ZoneTreeIdentityFile.Read(Path.Combine(ReplicaDirectory, ZoneTreePersistenceFormat.IdentityFileName),
            limits.MaximumIdentityFileBytes, limits.StreamBufferBytes);
        if (replica.NodeId == originalIdentity.NodeId || replica.Incarnation != originalIdentity.Incarnation
            || !CryptographicOperations.FixedTimeEquals(replica.SigningKey.Span, originalIdentity.SigningKey.Span))
        {
            throw new InvalidOperationException("The original native replica identity is not the canonical owner's distinct replica.");
        }
        return replica;
    }

    private void Join()
    {
        if (!originalJoined)
        {
            originalJoined = true;
            Source.JoinOwnedResources();
            return;
        }
        var admission = coldAdmission;
        var store = coldStore;
        coldAdmission = null;
        coldStore = null;
        coldDatabase = null;
        var failures = new List<Exception>();
        if (admission is not null)
        {
            ServerFailureObserver.Observe(admission.Dispose, failures);
        }
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void DeleteJoinedDirectories()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => Directory.Delete(ReplicaDirectory, true), failures);
        ServerFailureObserver.Observe(() => Directory.Delete(Source.Directory, true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
