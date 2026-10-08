using KeyLoad.Core;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.CrashHost.Features.Search;

internal sealed class NativeAnnCrashCommandOwner : IDisposable
{
    private const string ReplicaDirectory = "ann-process-replica";
    private const string Voter = "crash-a";
    private const string SecondVoter = "crash-b";
    private const string ThirdVoter = "crash-c";
    private const long FirstTerm = 1;
    private const string MissingEntry = "The native fixture retained log entry is missing.";
    private const long IndexStep = 1;
    private readonly Lock admission = new();
    private readonly ZoneTreeStore replica;
    private readonly DurableReplicaLog log;
    private readonly ReplicaMaterializer materializer;
    private readonly ReplicaExecutionOptions execution;

    internal NativeAnnCrashCommandOwner(string directory, DatabaseEngine database)
    {
        Database = database;
        var configuration = CrashExecutionOptions.Configuration(Voter,
            [Voter, SecondVoter, ThirdVoter], Path.Combine(directory, ReplicaDirectory), database.Store.Identity.Incarnation);
        var options = CrashExecutionOptions.Replica();
        execution = options.Value;
        replica = new(new(configuration.Value.Directory)
        { Incarnation = configuration.Value.Incarnation, SigningKey = database.Store.Identity.SigningKey },
            CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        try
        {
            log = new(replica, configuration, canonicalDatabase: database);
            try
            {
                var snapshots = new ReplicaSnapshotStore(database.Store, log, configuration, options);
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

    internal DatabaseEngine Database { get; }

    internal OperationResult Submit(ReplicatedOperation operation, bool explicitTime)
    {
        lock (admission)
        {
            using var timeout = new CancellationTokenSource(execution.CommandTimeout, TimeProvider.System);

            var actual = Database.NormalizeOperation(explicitTime ? operation
                : operation with { EvaluatedAt = Database.EvaluationClock.GetUtcNow() });
            if (RetainedCommandIndex(actual.Id) is { } originalIndex)
            {
                if (originalIndex > log.State.CommittedIndex)
                { materializer.Commit(originalIndex); }
                materializer.WaitForApplyAsync(originalIndex, timeout.Token).GetAwaiter().GetResult();
                return Database.ResolveOutcome(actual);
            }
            if (log.State.Term < FirstTerm)
            { log.SaveTermAndVote(FirstTerm, Voter); }
            var index = checked(log.State.LastIndex + IndexStep);
            log.Append([new(index, log.State.Term, actual)]);
            materializer.Commit(index);
            materializer.WaitForApplyAsync(index, timeout.Token).GetAwaiter().GetResult();
            return Database.ResolveOutcome(actual);
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
