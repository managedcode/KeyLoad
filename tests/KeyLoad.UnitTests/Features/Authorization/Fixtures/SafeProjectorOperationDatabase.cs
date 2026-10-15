using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class SafeProjectorOperationDatabase
{
    private const string MissingOwner = "The original safe projector native owner is not open.";
    private const string WrongReplica = "The safe projector replica identity differs from its original owner.";
    private readonly StoreIdentity canonicalIdentity;
    private StoreIdentity? replicaIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    private TestDatabaseReplicaAdmission? coldAdmission;
    private readonly string replicaDirectory;
    internal CancellationToken CallerToken
    {
        get;
    }
    internal TestDatabase Source
    {
        get;
    }
    internal ZoneTreeStore Store => originalJoined ? coldStore ?? throw new InvalidOperationException(MissingOwner) : Source.Store;
    internal DatabaseEngine Database => originalJoined ? coldDatabase ?? throw new InvalidOperationException(MissingOwner) : Source.Database;

    private SafeProjectorOperationDatabase(string directory, CancellationToken callerToken)
    {
        CallerToken = callerToken;
        callerToken.ThrowIfCancellationRequested();
        replicaDirectory = directory + TestDatabaseReplicaDirectory.Suffix;
        Source = new(directory: directory, nativeReplicaAdmission: true, nativeReplicaDirectory: replicaDirectory);
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
            owner.replicaIdentity = owner.ReadReplicaIdentity();
            await operation(owner);
        }, failures);
        if (owner is not null)
        {
            ServerFailureObserver.Observe(owner.Join, failures);
        }
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => Delete(directory + TestDatabaseReplicaDirectory.Suffix), failures);
            ServerFailureObserver.Observe(() => Delete(directory), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal OperationResult Submit(ReplicatedOperation operation)
    {
        CallerToken.ThrowIfCancellationRequested();
        return originalJoined
            ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).Submit(operation, explicitTime: true, cancellationToken: CallerToken)
            : Source.SubmitIssued(operation);
    }

    internal ReplicaEntry Entry(ReplicatedOperation operation) => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).ReadRetainedEntry(operation)
        : Source.ReadRetainedEntry(operation);

    internal (long LastIndex, long CommittedIndex, long Term) JournalCut() => originalJoined
        ? (coldAdmission ?? throw new InvalidOperationException(MissingOwner)).ReadJournalCut() : Source.ReadJournalCut();

    internal CommitToken NextToken()
    {
        var placement = Database.ReadAtomicPartitionPlacement(SafeProjectorOperationProtocol.Root,
            new(SafeProjectorOperationProtocol.PlacementVersion, Source.Partition));
        return new(placement.Incarnation, Source.Partition.AtomicPartitionId,
            checked(JournalCut().LastIndex + SafeProjectorOperationProtocol.NextJournalIndex), placement.PlacementEpoch);
    }

    internal async Task ReopenAsync()
    {
        CallerToken.ThrowIfCancellationRequested();
        Join();
        var expectedReplica = replicaIdentity ?? throw new InvalidOperationException(MissingOwner);
        await IdentityAsync(ReadReplicaIdentity(), expectedReplica);
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = canonicalIdentity.Incarnation
        }, canonicalIdentity.NodeId, Source.StorageExecution);
        await IdentityAsync(coldStore.Identity, canonicalIdentity);
        coldDatabase = Source.CreateConfiguredDatabase(coldStore);
        coldAdmission = Source.CreateTargetAdmission(coldDatabase, coldStore,
            Source.Directory, replicaDirectory, expectedReplica.NodeId);
    }

    private StoreIdentity ReadReplicaIdentity()
    {
        var limits = Source.StorageExecution.Value;
        var value = ZoneTreeIdentityFile.Read(Path.Combine(replicaDirectory, ZoneTreePersistenceFormat.IdentityFileName),
            limits.MaximumIdentityFileBytes, limits.StreamBufferBytes);
        if (value.NodeId == canonicalIdentity.NodeId || value.Incarnation != canonicalIdentity.Incarnation
            || !CryptographicOperations.FixedTimeEquals(value.SigningKey.Span, canonicalIdentity.SigningKey.Span))
        {
            throw new InvalidOperationException(WrongReplica);
        }
        return value;
    }

    private static async Task IdentityAsync(StoreIdentity actual, StoreIdentity expected)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

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

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
