using System.Diagnostics.CodeAnalysis;
using KeyLoad.Core.CommandOutcomes.Contracts;
using KeyLoad.Core.Search;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25OriginalPhaseRecoveryOwner
{
    private const string DirectoryPrefix = "keyload-bm25-phase-recovery-";
    private const string MissingOwner = "The original BM25 execution owner is not open.";
    private readonly StoreIdentity originalIdentity;
    private bool originalJoined;
    private ZoneTreeStore? coldStore;

    internal TestDatabase Source
    {
        get;
    }

    private Bm25OriginalPhaseRecoveryOwner()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Source = new(directory: directory);
        originalIdentity = Source.Store.Identity;
    }

    internal ZoneTreeStore Store => originalJoined
        ? coldStore ?? throw new InvalidOperationException(MissingOwner) : Source.Store;

    [AllowNull]
    internal DatabaseEngine Database
    {
        get => originalJoined
        ? field ?? throw new InvalidOperationException(MissingOwner) : Source.Database; private set;
    }

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

    internal StoredOutcome PhaseEntry(Command<DatabaseCommand> operation)
    {
        if (operation.Value is not { Kind: OperationKind.Bm25CorpusPublicationPhase } input)
        {
            throw new InvalidOperationException("The original BM25 phase command is required.");
        }
        var phase = ((Bm25CorpusPublicationPhaseCommand)input.Input);
        if (phase.CommandId != operation.CommandId || phase.Current.Partition != Source.Partition)
        {
            throw new InvalidOperationException("The original BM25 phase partition is inconsistent.");
        }
        var key = OutcomeStoreOracle.PartitionKey(phase.Current.Partition, operation.UserId!, operation.CommandId);
        var inspection = Source.CreateReadWork(Bm25OriginalPhaseRecoveryFlow.Token);
        var stored = Store.Read(raw => inspection.CreateView(raw).GetRecord<StoredOutcome>(key));
        if (stored is null || stored.ScopeKind != CommandOutcomeScopeKind.Partition
            || stored.Partition != phase.Current.Partition || stored.PreparedCommand is not { } prepared
            || prepared.CommandId != operation.CommandId || prepared.UserId != operation.UserId
            || prepared.Value is not { } actual || actual.Kind != input.Kind
            || !OrleansSerialization.Serialize(actual.Input, ValidationProfile.PublicInputElements).AsSpan().SequenceEqual(OrleansSerialization.Serialize(input.Input, ValidationProfile.PublicInputElements).AsSpan()))
        {
            throw new InvalidOperationException("The exact original BM25 partition outcome is missing or inconsistent.");
        }
        return stored;
    }

    internal (long AppliedPosition, long StorePosition) ReadCut()
        => Store.Read(view => (view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
            ? OrleansSerialization.Deserialize<long>(bytes) : default(long), Store.Position));

    internal void Reopen()
    {
        Join();
        coldStore = ZoneTreeExistingStore.Open(new(Source.Directory)
        {
            Incarnation = originalIdentity.Incarnation
        }, originalIdentity.NodeId, Source.StorageExecution);
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
