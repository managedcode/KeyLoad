using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using D = KeyLoad.UnitTests.Features.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

// Real ordered native admission, with the original canonical and replica directories joined before reopen.
internal sealed class CompositionPrefixPrepareOwner
{
    private readonly StoreIdentity identity;
    private readonly StoreIdentity replicaIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    private TestDatabaseReplicaAdmission? coldAdmission;
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
        try
        {
            var storage = Source.StorageExecution.Value;
            replicaIdentity = ZoneTreeIdentityFile.Read(Path.Combine(directory + TestDatabaseReplicaDirectory.Suffix,
                ZoneTreePersistenceFormat.IdentityFileName), storage.MaximumIdentityFileBytes, storage.StreamBufferBytes);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(Source.JoinOwnedResources, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
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
                ServerFailureObserver.Observe(() => Directory.Delete(owner.Source.Directory + TestDatabaseReplicaDirectory.Suffix, true), failures);
                ServerFailureObserver.Observe(() => Directory.Delete(owner.Source.Directory, true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal OperationResult Submit(ReplicatedOperation original)
    {
        Token.ThrowIfCancellationRequested();
        return originalJoined ? coldAdmission!.Submit(original, explicitTime: true, cancellationToken: Token)
            : Source.SubmitIssued(original);
    }

    internal (long LastIndex, long CommittedIndex, long Term) Cut() => originalJoined
        ? coldAdmission!.ReadJournalCut() : Source.ReadJournalCut();

    internal ReplicatedOperation Retained(ReplicatedOperation original) => (originalJoined
        ? coldAdmission!.ReadRetainedEntry(original) : Source.ReadRetainedEntry(original)).Operation!;

    internal CommitToken NextToken()
    {
        var placement = Database.ReadAtomicPartitionPlacement(D.Root, Source.Partition);
        return new(placement.Incarnation, Source.Partition.AtomicPartitionId,
            checked(Cut().LastIndex + D.FirstRevision), placement.PlacementEpoch);
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
        var storage = Source.StorageExecution.Value;
        var currentReplica = ZoneTreeIdentityFile.Read(Path.Combine(Source.Directory + TestDatabaseReplicaDirectory.Suffix,
            ZoneTreePersistenceFormat.IdentityFileName), storage.MaximumIdentityFileBytes, storage.StreamBufferBytes);
        await CompositionPrefixPrepareAssertions.EqualAsync(replicaIdentity, currentReplica);
        coldDatabase = Source.CreateConfiguredDatabase(coldStore);
        coldAdmission = Source.CreateTargetAdmission(coldDatabase, coldStore, Source.Directory,
            Source.Directory + TestDatabaseReplicaDirectory.Suffix, replicaIdentity.NodeId);
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
}
