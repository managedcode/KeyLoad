using Microsoft.Extensions.Options;
using KeyLoad.Diagnostics.Features.ResourceExecution;

namespace KeyLoad.Replication;

internal sealed class ReplicaState
{
    private const int FirstElectionTerm = 1;
    private const int NoLeaderReadyPosition = 0;
    private const int FirstLogPosition = 1;
    private const int BeforeFirstLogPosition = 0;

    private readonly ReplicaConfiguration configuration;
    internal ReplicaMaterializer Materializer { get; }
    internal ReplicaConfiguration Configuration => configuration;
    internal TimeProvider Clock { get; }

    internal ReplicaState(ReplicaMaterializer materializer, IOptions<ReplicaConfiguration> options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        configuration = options.Value;
        configuration.Validate();
        Materializer = materializer;
        Clock = clock;
        electionStarted = clock.GetTimestamp();
        electionTimeout = ReplicaElectionTimeout.Select(configuration);
    }
    internal IDurableReplicaLog Log => Materializer.Log;
    internal ReplicaRole Role { get; set; } = ReplicaRole.Follower;
    internal string? LeaderId { get; set; }
    internal long LeaderReadyIndex { get; set; }
    internal Dictionary<string, ReplicaProgress> Progress { get; } = new(StringComparer.Ordinal);
    internal volatile bool Ready;
    private long electionStarted;
    private TimeSpan electionTimeout;
    private Exception? failure;

    internal async Task<T> LockedAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var waitStarted = DatabasePhaseTelemetry.Begin();
        var waitOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            await Materializer.ProtocolGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            waitOutcome = DatabasePhaseOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            waitOutcome = DatabasePhaseTelemetry.CancellationOutcome(cancellationToken);
            throw;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.ReplicaProtocolGateWait, waitOutcome, waitStarted);
        }

        var holdStarted = DatabasePhaseTelemetry.Begin();
        var holdOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            Check();
            var result = action();
            holdOutcome = DatabasePhaseOutcome.Completed;
            return result;
        }
        finally
        {
            var releaseOutcome = DatabasePhaseOutcome.Faulted;
            try
            {
                Materializer.ProtocolGate.Release();
                releaseOutcome = holdOutcome;
            }
            finally
            {
                DatabasePhaseTelemetry.End(DatabasePhaseKind.ReplicaProtocolGateHold, releaseOutcome, holdStarted);
            }
        }
    }

    internal void Check()
    {
        if (Volatile.Read(ref failure) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        }
    }

    internal void Poison(Exception error) => Interlocked.CompareExchange(ref failure, error, null);
    internal bool ElectionDue => Clock.GetElapsedTime(electionStarted) >= electionTimeout;
    internal void ResetElection()
    {
        electionStarted = Clock.GetTimestamp();
        electionTimeout = ReplicaElectionTimeout.Select(Configuration);
    }

    internal bool ObserveLeader(long term, string leader)
    {
        var previousTerm = Log.State.Term;
        if (term < previousTerm)
        {
            return false;
        }
        if (term < FirstElectionTerm || !Configuration.VoterIds.Contains(leader, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
        if (term > Log.State.Term)
        {
            Log.SaveTermAndVote(term, null);
        }
        if (Role == ReplicaRole.Leader && term == previousTerm && leader != Configuration.LocalId)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidPeer);
        }
        Role = ReplicaRole.Follower;
        LeaderId = leader;
        LeaderReadyIndex = NoLeaderReadyPosition;
        ResetElection();
        return true;
    }

    internal void ObserveTerm(long term)
    {
        if (term <= Log.State.Term)
        {
            return;
        }
        Log.SaveTermAndVote(term, null);
        Role = ReplicaRole.Follower;
        LeaderId = null;
        LeaderReadyIndex = NoLeaderReadyPosition;
        ResetElection();
    }

    internal void RequireLeader(long? term = null)
    {
        if (Role != ReplicaRole.Leader || term is { } expected && expected != Log.State.Term)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
    }

    internal void RequireReadyLeader()
    {
        RequireLeader();
        if (LeaderReadyIndex < FirstLogPosition || Log.State.CommittedIndex < LeaderReadyIndex)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
    }

    internal ReplicaNodeState Snapshot()
    {
        var log = Log.State;
        return new(Configuration.LocalId, LeaderId, Role, log.Term, log.LastIndex, log.CommittedIndex,
            Materializer.Database.LastApplied, log.Snapshot?.Index ?? BeforeFirstLogPosition, Ready && Volatile.Read(ref failure) is null);
    }
}

internal sealed record ReplicaProgress(long NextIndex, long MatchedIndex);
