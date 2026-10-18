using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

// Reopens the actual canonical ZoneTree owner only after its original file lifetime has joined.
internal sealed class CompositionPrefixPrepareOwner
{
    private readonly StoreIdentity identity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    internal TestDatabase Source
    {
        get;
    }
    internal CancellationToken Token
    {
        get;
    }
    internal ZoneTreeStore Store => originalJoined ? coldStore! : Source.Store;
    internal DatabaseEngine Database => originalJoined ? coldDatabase! : Source.Database;
    internal Dictionary<string, ResourceLifetimeV1> Lifetimes { get; } = new(StringComparer.Ordinal);
    internal List<CompositionPrefixPrepareCommand> Commands { get; } = [];

    private CompositionPrefixPrepareOwner(DatabaseLimits? limits, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Token = token;
        var directory = Path.Combine(Path.GetTempPath(), D.DirectoryPrefix + Guid.NewGuid().ToString(D.GuidFormat));
        Source = new(limits, directory);
        identity = Source.Store.Identity;

    }

    internal static async Task RunAsync(DatabaseLimits? limits,
        Func<CompositionPrefixPrepareOwner, Task> operation, CancellationToken token)
    {
        CompositionPrefixPrepareOwner? owner = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            owner = new(limits, token);
            await operation(owner);
        }, failures);
        if (owner is not null)
        {
            ServerFailureObserver.Observe(owner.Join, failures);
            if (failures.Count == D.Absent)
            {
                ServerFailureObserver.Observe(() => Directory.Delete(owner.Source.Directory, true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Result<object> Submit(Command<DatabaseCommand> original)
    {
        Token.ThrowIfCancellationRequested();
        return originalJoined ? Database.Apply(original)
            : Source.SubmitIssued(original);
    }

    internal (long AppliedPosition, long StorePosition) Cut()
        => Store.Read(_ => (Database.LastApplied, Store.Position));

    internal Command<DatabaseCommand> Retained(Command<DatabaseCommand> original) => (originalJoined
        ? OutcomeStoreOracle.ReadStored(Store, original) ?? throw new InvalidOperationException("The exact stored command is missing.") : Source.ReadStoredOutcome(original)).PreparedCommand;

    internal CommitToken NextToken()
    {
        var placement = Database.ReadAtomicPartitionPlacement(D.Root, Source.Partition);
        return new(placement.Incarnation, Source.Partition.AtomicPartitionId,
            checked(Cut().AppliedPosition + D.FirstRevision));
    }

    internal async Task ReopenAsync()
    {
        Join();
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = identity.Incarnation
        },
            identity.NodeId, Source.StorageExecution);
        await CompositionPrefixPrepareAssertions.EqualAsync(identity, coldStore.Identity);
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
}
