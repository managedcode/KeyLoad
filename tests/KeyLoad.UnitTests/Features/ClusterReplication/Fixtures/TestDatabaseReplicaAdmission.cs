using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;

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
    private readonly ReplicaMaterializer materializer;
    private readonly ReplicaExecutionOptions execution;

    internal TestDatabaseReplicaAdmission(TestDatabase owner)
    {
        database = owner.Database;
        var configuration = UnitExecutionOptions.ReplicaConfiguration(new(Voter,
            PhysicalShardCatalogVoterIds.Standard, Path.Combine(owner.Directory, ReplicaDirectory), owner.Store.Identity.Incarnation));
        var options = UnitExecutionOptions.ReplicaExecution();
        execution = options.Value;
        replica = new(new(configuration.Value.Directory)
        { Incarnation = configuration.Value.Incarnation, SigningKey = owner.Store.Identity.SigningKey },
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        try
        {
            log = new(replica, configuration, canonicalDatabase: database);
            try
            {
                var snapshots = new ReplicaSnapshotStore(owner.Store, log, configuration, options);
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

    internal OperationResult Submit(ReplicatedOperation operation, bool explicitTime)
    {
        lock (admission)
        {
            using var timeout = new CancellationTokenSource(execution.CommandTimeout, TimeProvider.System);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None, timeout.Token);
            var actual = database.NormalizeOperation(explicitTime ? operation
                : operation with { EvaluatedAt = database.EvaluationClock.GetUtcNow() });
            if (RetainedCommandIndex(actual.Id) is { } originalIndex)
            {
                if (originalIndex > log.State.CommittedIndex)
                { materializer.Commit(originalIndex); }
                materializer.WaitForApplyAsync(originalIndex, cancellation.Token).GetAwaiter().GetResult();
                return database.ResolveOutcome(actual);
            }
            if (log.State.Term < FirstTerm)
            { log.SaveTermAndVote(FirstTerm, Voter); }
            var index = checked(log.State.LastIndex + IndexStep);
            log.Append([new(index, log.State.Term, actual)]);
            materializer.Commit(index);
            materializer.WaitForApplyAsync(index, cancellation.Token).GetAwaiter().GetResult();
            return database.ResolveOutcome(actual);
        }
    }

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
        try
        { materializer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        try
        { log.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        try
        { replica.Dispose(); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
