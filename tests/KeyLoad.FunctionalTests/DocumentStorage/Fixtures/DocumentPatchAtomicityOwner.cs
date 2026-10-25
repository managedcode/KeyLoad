using System.Diagnostics.CodeAnalysis;
using KeyLoad.ProcessRecoveryTests.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal sealed class DocumentPatchAtomicityOwner
{
    private const string DirectoryPrefix = "keyload-document-patch-atomic-";
    private const string MissingOwner = "The original document execution owner is not open.";
    private readonly StoreIdentity originalIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;

    internal TestDatabase Source
    {
        get;
    }

    private DocumentPatchAtomicityOwner()
    {
        var directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
            DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Source = new(directory: directory);
        originalIdentity = Source.Store.Identity;
    }

    internal ZoneTreeStore Store => originalJoined
        ? coldStore ?? throw new InvalidOperationException(MissingOwner) : Source.Store;

    [AllowNull]
    internal DatabaseEngine Database
    {
        get => originalJoined
            ? field ?? throw new InvalidOperationException(MissingOwner) : Source.Database;
        private set;
    }

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

    internal Result<object> Submit(Command<DatabaseCommand> operation) => Database.Apply(operation);

    internal StoredOutcome Entry(Guid id) => originalJoined
        ? OutcomeStoreOracle.ReadStored(Database, id, CancellationToken.None)
        : Source.ReadStoredOutcome(id);

    internal StoredOutcome Entry(Command<DatabaseCommand> original) => originalJoined
        ? OutcomeStoreOracle.ReadStored(Store, original) ?? throw new InvalidOperationException(MissingOwner)
        : Source.ReadStoredOutcome(original);

    internal (long AppliedPosition, long StorePosition) ReadCut()
        => Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), Store.Position));

    internal async Task ReopenAsync()
    {
        Join();
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = originalIdentity.Incarnation
        }, originalIdentity.NodeId, Source.StorageExecution);
        await DocumentPatchAtomicityAssertions.BytesAsync(OrleansSerialization.Serialize(originalIdentity),
            OrleansSerialization.Serialize(coldStore.Identity));
        Database = Source.CreateConfiguredDatabase(coldStore);
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
        Database = null;
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
