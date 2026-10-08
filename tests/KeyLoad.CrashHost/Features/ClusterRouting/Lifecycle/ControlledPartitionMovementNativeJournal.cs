using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

/// <summary>Owns one actual native replica journal/materializer for a movement fixture's canonical owner.</summary>
internal sealed class ControlledPartitionMovementNativeJournal : IDisposable
{
    private const long FirstTerm = 1;
    private const long IndexStep = 1;
    /// <summary>Maximum original supporting fixture history.</summary>
    public const long SupportingHistoryEntries = 64;
    /// <summary>Frozen mathematical terminal fixture history; native per-append ceilings remain unchanged.</summary>
    public const long TerminalHistoryEntries = 426;
    private readonly long maximumFixtureEntries;
    private const long FirstEntry = 1;
    private const string MissingEntry = "The original movement fixture replica entry is missing.";
    private readonly Lock gate = new();
    private readonly DatabaseEngine database;
    private readonly ZoneTreeStore replica;
    private readonly ReplicaMaterializer materializer;
    private readonly IOptions<ReplicaExecutionOptions> execution;
    private readonly IOptions<ReplicaConfiguration> configuration;
    private readonly string voter;
    private bool closed;

    /// <summary>Opens the original configured native replica journal and materializer.</summary>
    /// <param name="database">The actual borrowed canonical database owner.</param>
    /// <param name="physical">Its immutable actual physical RF3 logical scope.</param>
    /// <param name="directory">The independently owned replica-store directory.</param>
    /// <param name="maximumFixtureEntries">The exact supporting or terminal history bound.</param>
    public ControlledPartitionMovementNativeJournal(DatabaseEngine database,
        PhysicalShardRecord physical, string directory, long maximumFixtureEntries = SupportingHistoryEntries)
    {
        if (maximumFixtureEntries != SupportingHistoryEntries && maximumFixtureEntries != TerminalHistoryEntries)
        { throw new ArgumentOutOfRangeException(nameof(maximumFixtureEntries)); }
        this.maximumFixtureEntries = maximumFixtureEntries;
        this.database = database;
        voter = physical.VoterIds.First();
        configuration = CrashExecutionOptions.Configuration(voter, physical.VoterIds.ToArray(), directory, physical.Incarnation);
        execution = CrashExecutionOptions.Replica();
        replica = new(new(directory)
        { Incarnation = physical.Incarnation, SigningKey = database.Store.Identity.SigningKey },
            CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        try
        {
            Log = new(replica, configuration, canonicalDatabase: database);
            try
            {
                var snapshots = new ReplicaSnapshotStore(database.Store, Log, configuration, execution);
                materializer = new(database, Log, snapshots, execution);
            }
            catch (Exception primary)
            {
                var failures = new List<Exception> { primary };
                try
                { Log.Dispose(); }
                catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
                catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
                ServerFailureObserver.ThrowIfAny(failures);
                throw;
            }
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            try
            { replica.Dispose(); }
            catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    /// <summary>The actual validated configuration of the original replica store.</summary>
    public ReplicaConfiguration Configuration => configuration.Value;
    /// <summary>The original native log, borrowed for verified control-grant evidence.</summary>
    public DurableReplicaLog Log { get; }

    /// <summary>Appends an unseen scoped command or resolves its original joined native entry.</summary>
    /// <param name="operation">The original native local-signed operation.</param>
    /// <param name="caller">The original fixture operation cancellation token.</param>
    /// <returns>The fresh-authorized actual durable outcome.</returns>
    public OperationResult Submit(ReplicatedOperation operation, CancellationToken caller)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            using var deadline = new CancellationTokenSource(execution.Value.CommandTimeout, database.EvaluationClock);
            using var original = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
            original.Token.ThrowIfCancellationRequested();
            var actual = database.NormalizeOperation(operation);
            var index = FindOriginal(actual, original.Token);
            if (index is null)
            {
                if (Log.State.Term < FirstTerm)
                { Log.SaveTermAndVote(FirstTerm, voter); }
                index = checked(Log.State.LastIndex + IndexStep);
                if (index > maximumFixtureEntries)
                { throw new InvalidOperationException(MissingEntry); }
                Log.Append([new(index.Value, Log.State.Term, actual)]);
            }
            if (index > Log.State.CommittedIndex)
            { materializer.Commit(index.Value); }
            materializer.WaitForApplyAsync(index.Value, original.Token).GetAwaiter().GetResult();
            return database.ResolveOutcome(actual);
        }
    }

    private long? FindOriginal(ReplicatedOperation actual, CancellationToken cancellationToken)
    {
        var lastIndex = Log.State.LastIndex;
        if (lastIndex > maximumFixtureEntries)
        { throw new InvalidOperationException(MissingEntry); }
        var scope = CommandOutcomePartitionIdentity.Resolve(actual);
        var firstIndex = FirstEntry;
        while (firstIndex <= lastIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = Log.Read(firstIndex, configuration.Value.MaxAppendEntries,
                configuration.Value.MaxAppendBytes);
            if (page.IsDefaultOrEmpty)
            { throw new InvalidOperationException(MissingEntry); }
            foreach (var entry in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Index != firstIndex || entry.Index > lastIndex)
                { throw new InvalidOperationException(MissingEntry); }
                if (entry.Operation is { } previous && previous.Id == actual.Id
                    && previous.PrincipalId == actual.PrincipalId
                    && CommandOutcomePartitionIdentity.Resolve(previous) == scope)
                { return entry.Index; }
                firstIndex = checked(entry.Index + IndexStep);
            }
        }
        return null;
    }

    /// <summary>Joins original materialization and disposes every independently owned native journal resource.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (closed)
            { return; }
            closed = true;
            var failures = new List<Exception>();
            try
            { materializer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            try
            { Log.Dispose(); }
            catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            try
            { replica.Dispose(); }
            catch (Exception error) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            catch (Exception error) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
