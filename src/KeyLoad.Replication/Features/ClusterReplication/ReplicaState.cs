namespace KeyLoad.Replication;

internal sealed class ReplicaState(ReplicaMaterializer materializer, ReplicaConfiguration configuration, TimeProvider clock)
{
    internal ReplicaMaterializer Materializer { get; } = materializer;
    internal ReplicaConfiguration Configuration { get; } = configuration;
    internal TimeProvider Clock { get; } = clock;
    internal IDurableReplicaLog Log => Materializer.Log;
    internal ReplicaRole Role { get; set; } = ReplicaRole.Follower;
    internal string? LeaderId { get; set; }
    internal long LeaderReadyIndex { get; set; }
    internal Dictionary<string, ReplicaProgress> Progress { get; } = new(StringComparer.Ordinal);
    internal volatile bool Ready;
    private long electionStarted = clock.GetTimestamp();
    private TimeSpan electionTimeout = ReplicaElectionTimeout.Select(configuration);
    private Exception? failure;

    internal async Task<T> LockedAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        await Materializer.ProtocolGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        { Check(); return action(); }
        finally { Materializer.ProtocolGate.Release(); }
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
        if (term < 1 || !Configuration.VoterIds.Contains(leader, StringComparer.Ordinal))
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
        LeaderReadyIndex = 0;
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
        LeaderReadyIndex = 0;
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
        if (LeaderReadyIndex < 1 || Log.State.CommittedIndex < LeaderReadyIndex)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
        }
    }

    internal ReplicaNodeState Snapshot()
    {
        var log = Log.State;
        return new(Configuration.LocalId, LeaderId, Role, log.Term, log.LastIndex, log.CommittedIndex,
            Materializer.Database.LastApplied, log.Snapshot?.Index ?? 0, Ready && Volatile.Read(ref failure) is null);
    }
}

internal sealed record ReplicaProgress(long NextIndex, long MatchedIndex);
