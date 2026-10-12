using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentPatchAtomicityOwner
{
    private const string DirectoryPrefix = "keyload-document-patch-atomic-";
    private const string MissingOwner = "The original document native owner is not open.";
    private readonly StoreIdentity originalIdentity;
    private StoreIdentity originalReplicaIdentity = null!;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    private TestDatabaseReplicaAdmission? coldAdmission;
    internal TestDatabase Source { get; }
    private string ReplicaDirectory => Source.Directory + TestDatabaseReplicaDirectory.Suffix;

    private DocumentPatchAtomicityOwner()
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

    internal static async Task RunAsync(Func<DocumentPatchAtomicityOwner, Task> operation)
    {
        var failures = new List<Exception>();
        DocumentPatchAtomicityOwner? owner = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner = new();
            owner.originalReplicaIdentity = owner.ReadReplicaIdentity();
            await operation(owner);
        }, failures);
        if (owner is not null)
        {
            ServerFailureObserver.Observe(owner.Join, failures);
            if (failures.Count == 0)
            { ServerFailureObserver.Observe(owner.DeleteJoinedDirectories, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal OperationResult Submit(ReplicatedOperation operation) => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).Submit(operation, explicitTime: true)
        : Source.SubmitIssued(operation);

    internal ReplicaEntry Entry(Guid id) => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).ReadRetainedEntry(id)
        : Source.ReadRetainedEntry(id);

    internal (long LastIndex, long CommittedIndex, long Term) JournalCut() => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).ReadJournalCut()
        : Source.ReadJournalCut();

    internal async Task ReopenAsync()
    {
        Join();
        await DocumentPatchAtomicityAssertions.BytesAsync(NativeSerialization.Serialize(originalReplicaIdentity),
            NativeSerialization.Serialize(ReadReplicaIdentity()));
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        { Incarnation = originalIdentity.Incarnation }, originalIdentity.NodeId, Source.StorageExecution);
        await DocumentPatchAtomicityAssertions.BytesAsync(NativeSerialization.Serialize(originalIdentity),
            NativeSerialization.Serialize(coldStore.Identity));
        coldDatabase = Source.CreateConfiguredDatabase(coldStore);
        coldAdmission = Source.CreateTargetAdmission(coldDatabase, coldStore,
            Source.Directory, ReplicaDirectory, originalReplicaIdentity.NodeId);
    }

    private StoreIdentity ReadReplicaIdentity()
    {
        var limits = Source.StorageExecution.Value;
        var replica = ZoneTreeIdentityFile.Read(Path.Combine(ReplicaDirectory, ZoneTreePersistenceFormat.IdentityFileName),
            limits.MaximumIdentityFileBytes, limits.StreamBufferBytes);
        if (replica.NodeId == originalIdentity.NodeId || replica.Incarnation != originalIdentity.Incarnation
            || !CryptographicOperations.FixedTimeEquals(replica.SigningKey.Span, originalIdentity.SigningKey.Span))
        { throw new InvalidOperationException("The original native replica identity is not the canonical owner\'s distinct replica."); }
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
        { ServerFailureObserver.Observe(admission.Dispose, failures); }
        if (store is not null)
        { ServerFailureObserver.Observe(store.Dispose, failures); }
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
