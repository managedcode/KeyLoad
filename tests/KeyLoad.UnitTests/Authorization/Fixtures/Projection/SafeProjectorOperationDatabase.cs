using System.Diagnostics.CodeAnalysis;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class SafeProjectorOperationDatabase
{
    private const string MissingOwner = "The original safe projector storage owner is not open.";
    private readonly StoreIdentity canonicalIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;

    internal CancellationToken CallerToken { get; }
    internal TestDatabase Source { get; }
    internal ZoneTreeStore Store => originalJoined ? coldStore ?? throw new InvalidOperationException(MissingOwner) : Source.Store;

    [AllowNull]
    internal DatabaseEngine Database { get => originalJoined ? field ?? throw new InvalidOperationException(MissingOwner) : Source.Database; private set; }

    private SafeProjectorOperationDatabase(string directory, CancellationToken callerToken)
    {
        CallerToken = callerToken;
        callerToken.ThrowIfCancellationRequested();
        Source = new(directory: directory);
        canonicalIdentity = Source.Store.Identity;
    }

    internal static async Task RunAsync(Func<SafeProjectorOperationDatabase, Task> operation, CancellationToken callerToken)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, SafeProjectorOperationProtocol.DirectoryPrefix
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
        return Source.SubmitAtOriginalTime(Database, operation);
    }

    internal StoredOutcome ReadOutcome(Command<DatabaseCommand> operation)
        => OutcomeStoreOracle.ReadStored(Store, operation) ?? throw new InvalidOperationException(MissingOwner);

    internal (long AppliedPosition, long StorePosition) ReadCanonicalCut()
        => Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), Store.Position));

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
        await Assert.That(OrleansSerialization.Serialize(coldStore.Identity).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(canonicalIdentity))).IsTrue();
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
        store?.Dispose();
    }
}
