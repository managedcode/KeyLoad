using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class SafeProjectorOperationDatabase
{
    private const string MissingOwner = "The original safe projector storage owner is not open.";
    private readonly StoreIdentity canonicalIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    internal CancellationToken CallerToken { get; }
    internal TestDatabase Source { get; }
    internal ZoneTreeStore Store => originalJoined ? coldStore ?? throw new InvalidOperationException(MissingOwner) : Source.Store;
    internal DatabaseEngine Database => originalJoined ? coldDatabase ?? throw new InvalidOperationException(MissingOwner) : Source.Database;

    private SafeProjectorOperationDatabase(string directory, CancellationToken callerToken)
    {
        CallerToken = callerToken;
        callerToken.ThrowIfCancellationRequested();
        Source = new(directory: directory);
        canonicalIdentity = Source.Store.Identity;
    }

    internal static async Task RunAsync(Func<SafeProjectorOperationDatabase, Task> operation, CancellationToken callerToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), SafeProjectorOperationProtocol.DirectoryPrefix
            + Guid.NewGuid().ToString(SafeProjectorOperationProtocol.GuidFormat));
        var failures = new List<Exception>();
        SafeProjectorOperationDatabase? owner = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner = new(directory, callerToken);
            await operation(owner);
        }, failures);
        if (owner is not null)
        {
            ServerFailureObserver.Observe(owner.Join, failures);
        }
        if (failures.Count == 0 && Directory.Exists(directory))
        {
            ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Result<object> Submit(Command<DatabaseCommand> operation)
    {
        CallerToken.ThrowIfCancellationRequested();
        return Database.Apply(operation);
    }

    internal StoredOutcome ReadOutcome(Command<DatabaseCommand> operation)
        => OutcomeStoreOracle.ReadStored(Store, operation) ?? throw new InvalidOperationException(MissingOwner);

    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => Store.Read(_ => (Database.LastApplied, Store.Position));

    internal CommitToken NextToken()
        => new(Store.Identity.Incarnation, Source.Partition.AtomicPartitionId,
            checked(Database.LastApplied + SafeProjectorOperationProtocol.NextJournalIndex));

    internal async Task ReopenAsync()
    {
        CallerToken.ThrowIfCancellationRequested();
        Join();
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = canonicalIdentity.Incarnation
        }, canonicalIdentity.NodeId, Source.StorageExecution);
        await Assert.That(NativeSerialization.Serialize(coldStore.Identity).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(canonicalIdentity))).IsTrue();
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
        store?.Dispose();
    }
}
