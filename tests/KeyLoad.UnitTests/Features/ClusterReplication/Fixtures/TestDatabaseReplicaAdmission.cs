using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests;

internal sealed class TestDatabaseReplicaAdmission : IDisposable
{
    private const string ReplicaDirectory = "ann-native-replica";
    private const string Voter = PhysicalShardCatalogVoterIds.First;
    private const long FirstTerm = 1;
    private const string MissingEntry = "The native fixture retained log entry is missing.";
    private const long IndexStep = 1;
    private readonly Lock admission = new();
    private readonly DatabaseEngine database;
    private readonly ZoneTreeStore replica;
    private readonly DurableReplicaLog log;
    private ReplicaMaterializer? materializer;
    private readonly IReplicaSnapshotStore snapshots;
    private readonly ReplicaExecutionOptions execution;

    internal TestDatabaseReplicaAdmission(TestDatabase owner, string? replicaDirectory = null)
        : this(owner.Database, owner.Store, owner.Directory, UnitExecutionOptions.ReplicaExecution(), replicaDirectory) { }

    internal TestDatabaseReplicaAdmission(DatabaseEngine database, ZoneTreeStore canonicalStore,
        string directory, IOptions<ReplicaExecutionOptions> options, string? replicaDirectory = null, Guid? originalNodeId = null)
    {
        this.database = database;
        var configuration = UnitExecutionOptions.ReplicaConfiguration(new(Voter,
            PhysicalShardCatalogVoterIds.Standard, replicaDirectory is null ? Path.Combine(directory, ReplicaDirectory)
                : TestDatabaseReplicaDirectory.Require(directory, replicaDirectory, originalNodeId), canonicalStore.Identity.Incarnation));
        ExecutionOptions = options;
        execution = options.Value;
        replica = new(new(configuration.Value.Directory)
        { Incarnation = configuration.Value.Incarnation, SigningKey = canonicalStore.Identity.SigningKey },
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        try
        {
            TestDatabaseReplicaDirectory.RequireOriginal(replica.Identity.NodeId, originalNodeId);
            log = new(replica, configuration, canonicalDatabase: database);
            try
            {
                snapshots = new ReplicaSnapshotStore(canonicalStore, log, configuration, options);
                materializer = new(database, log, snapshots, options);
            }
            catch (Exception primary)
            {
                try
                { log.Dispose(); }
                catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
                throw;
            }
        }
        catch (Exception primary)
        {
            try
            { replica.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    internal IOptions<ReplicaExecutionOptions> ExecutionOptions { get; }
    internal Guid JournalNodeId => replica.Identity.NodeId;

    internal OperationResult Submit(ReplicatedOperation operation, bool explicitTime, CancellationToken cancellationToken = default)
    {
        lock (admission)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contextToken = TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;
            using var caller = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(contextToken, cancellationToken) : null;
            using var timeout = new CancellationTokenSource(execution.CommandTimeout, TimeProvider.System);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                caller?.Token ?? contextToken, timeout.Token);
            var actual = database.NormalizeOperation(explicitTime ? operation
                : operation with { EvaluatedAt = database.EvaluationClock.GetUtcNow() });
            if (RetainedCommandIndex(actual.Id) is { } originalIndex)
            {
                if (originalIndex > log.State.CommittedIndex)
                { RequireMaterializer().Commit(originalIndex); }
                RequireMaterializer().WaitForApplyAsync(originalIndex, cancellation.Token).GetAwaiter().GetResult();
                return database.ResolveOutcome(actual);
            }
            if (log.State.Term < FirstTerm)
            { log.SaveTermAndVote(FirstTerm, Voter); }
            var index = checked(log.State.LastIndex + IndexStep);
            log.Append([new(index, log.State.Term, actual)]);
            RequireMaterializer().Commit(index);
            RequireMaterializer().WaitForApplyAsync(index, cancellation.Token).GetAwaiter().GetResult();
            return database.ResolveOutcome(actual);
        }
    }

    internal (long LastIndex, long CommittedIndex, long Term) ReadJournalCut()
    {
        lock (admission)
        { return (log.State.LastIndex, log.State.CommittedIndex, log.State.Term); }
    }

    internal ReplicaEntry ReadRetainedEntry(Guid originalId)
    {
        lock (admission)
        {
            var index = RetainedCommandIndex(originalId) ?? throw new InvalidOperationException(MissingEntry);
            return log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
        }
    }

    internal ReplicaEntry RecoverFaultOwnedRow(ReplicatedOperation original, Action restoreFaultOwnedRow,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        lock (admission)
        {
            using var timeout = new CancellationTokenSource(execution.CommandTimeout, TimeProvider.System);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            var entry = log.ReadEntry(log.State.CommittedIndex) ?? throw new InvalidOperationException(MissingEntry);
            materializer = TestDatabaseReplicaRecovery.Recover(RequireMaterializer(), database, log, snapshots, ExecutionOptions,
                database.NormalizeOperation(original), restoreFaultOwnedRow, () => materializer = null, failures, cancellation.Token);
            RequireMaterializer().WaitForApplyAsync(entry.Index, cancellation.Token).GetAwaiter().GetResult();
            return entry;
        }
    }

    private ReplicaMaterializer RequireMaterializer()
        => materializer ?? throw new InvalidOperationException(MissingEntry);

    private long? RetainedCommandIndex(Guid id)
    {
        for (var index = IndexStep; index <= log.State.LastIndex; index += IndexStep)
        {
            var entry = log.ReadEntry(index) ?? throw new InvalidOperationException(MissingEntry);
            if (entry.Operation?.Id == id)
            { return index; }
        }
        return null;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        Exception? fatal = null;
        try
        { materializer?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        { fatal = TestDatabaseJoinedLifetime.RetainFatal(failures, fatal, error); }
        try
        { log.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        { fatal = TestDatabaseJoinedLifetime.RetainFatal(failures, fatal, error); }
        try
        { replica.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null)
        { fatal = TestDatabaseJoinedLifetime.RetainFatal(failures, fatal, error); }
        TestDatabaseJoinedLifetime.ThrowIfAny(failures, fatal);
    }
}
