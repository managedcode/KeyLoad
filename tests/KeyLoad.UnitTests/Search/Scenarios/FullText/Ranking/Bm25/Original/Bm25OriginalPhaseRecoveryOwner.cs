using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
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
    private ZoneTreeStore? coldStore;
    private DatabaseEngine? coldDatabase;
    internal TestDatabase Source
    {
        get;
    }

    private Bm25OriginalPhaseRecoveryOwner()
    {
        var directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Source = new(directory: directory);
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

    internal Result<object> Submit(Command<DatabaseCommand> operation)
        => originalJoined
            ? Database.Apply(operation)
            : Source.SubmitIssued(operation);

    internal StoredOutcome Entry(Guid commandId) => originalJoined
        ? OutcomeStoreOracle.ReadStored(Database, commandId, CancellationToken.None)
        : Source.ReadStoredOutcome(commandId);

    internal (long AppliedPosition, long StorePosition) ReadCut()
        => Store.Read(_ => (Database.LastApplied, Store.Position));

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
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = originalIdentity.Incarnation
        }, originalIdentity.NodeId, Source.StorageExecution);
        coldDatabase = CreateColdDatabase(coldStore, configuredLimits);
    }

    private DatabaseEngine CreateColdDatabase(ZoneTreeStore store, DatabaseLimits? configuredLimits)
    {
        if (configuredLimits is null)
        {
            return Source.CreateConfiguredDatabase(store);
        }
        var create = TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(configuredLimits),
            UnitExecutionOptions.BlobExecution(), UnitExecutionOptions.NativeClaimsExecution(), null, Source.Database.EvaluationClock);
        return create(store);
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
