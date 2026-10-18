using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class DocumentPatchAtomicityOwner
{
    private const string DirectoryPrefix = "keyload-document-patch-atomic-";
    private const string MissingOwner = "The original document native owner is not open.";
    private readonly StoreIdentity originalIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    internal TestDatabase Source
    {
        get;
    }

    private DocumentPatchAtomicityOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Source = new(directory: directory);
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
            await operation(owner);
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

    internal Result<object> Submit(Command<DatabaseCommand> operation) => originalJoined
        ? Database.Apply(operation)
        : Source.SubmitIssued(operation);

    internal StoredOutcome Entry(Guid id) => originalJoined
        ? OutcomeStoreOracle.ReadStored(Database, id, CancellationToken.None)
        : Source.ReadStoredOutcome(id);

    internal StoredOutcome Entry(Command<DatabaseCommand> original) => originalJoined
        ? OutcomeStoreOracle.ReadStored(Store, original) ?? throw new InvalidOperationException(MissingOwner)
        : Source.ReadStoredOutcome(original);

    internal (long AppliedPosition, long StorePosition) ReadCut()
        => Store.Read(_ => (Database.LastApplied, Store.Position));

    internal async Task ReopenAsync()
    {
        Join();
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = originalIdentity.Incarnation
        }, originalIdentity.NodeId, Source.StorageExecution);
        await DocumentPatchAtomicityAssertions.BytesAsync(NativeSerialization.Serialize(originalIdentity),
            NativeSerialization.Serialize(coldStore.Identity));
        coldDatabase = Source.CreateConfiguredDatabase(coldStore);
    }

    private void Join()
    {
        if (!originalJoined)
        {
            originalJoined = true;
            Source.JoinOwnedResources();
            return;
        }
        var store = coldStore;
        coldStore = null;
        coldDatabase = null;
        var failures = new List<Exception>();
        if (store is not null)
        {
            ServerFailureObserver.Observe(store.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void DeleteJoinedDirectories()
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() => Directory.Delete(Source.Directory, true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
