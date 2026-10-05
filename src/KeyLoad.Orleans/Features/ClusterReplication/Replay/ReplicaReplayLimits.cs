namespace KeyLoad.Orleans;

/// <summary>Immutable per-voter nonce capacities; application traffic cannot spend consensus reserve.</summary>
[ConfigurationOptions]
public sealed record ReplicaReplayLimits
{
    private const int DefaultCriticalCapacity = 16_384;
    private const int DefaultForwardCapacity = 32_768;
    private const int DefaultReadBarrierCapacity = 16_384;
    private const int DefaultDataAppendCapacity = 32_768;
    /// <summary>Capacity for votes, snapshots, noops, heartbeats and database control commands.</summary>
    public int CriticalPerVoter { get; init; } = DefaultCriticalCapacity;
    /// <summary>Capacity for forwarded application operations.</summary>
    public int ForwardPerVoter { get; init; } = DefaultForwardCapacity;
    /// <summary>Capacity for application read barriers.</summary>
    public int ReadBarrierPerVoter { get; init; } = DefaultReadBarrierCapacity;
    /// <summary>Capacity for appends containing any application operation.</summary>
    public int DataAppendPerVoter { get; init; } = DefaultDataAppendCapacity;

    /// <summary>Returns the strict upper bound of retained nonce records for the fixed voter set.</summary>
    /// <param name="voterCount">The number of configured voters.</param>
    /// <returns>The aggregate maximum number of retained nonce records.</returns>
    public int MaximumRetainedNonces(int voterCount)
    {
        Validate(voterCount);
        return checked((int)(PerVoterTotal * voterCount));
    }

    /// <summary>Rejects nonpositive pools and aggregate memory budgets beyond the transport ceiling.</summary>
    /// <param name="voterCount">The number of configured voters.</param>
    public void Validate(int voterCount)
    {
        if (voterCount < 1 || CriticalPerVoter < 1 || ForwardPerVoter < 1 || ReadBarrierPerVoter < 1 || DataAppendPerVoter < 1
            || PerVoterTotal > ReplicaTransportProtocol.MaximumRetainedReplayNonces / voterCount)
        {
            throw new InvalidOperationException(ReplicaTransportProtocol.InvalidReplayLimits);
        }
    }

    private long PerVoterTotal => (long)CriticalPerVoter + ForwardPerVoter + ReadBarrierPerVoter + DataAppendPerVoter;

    internal int Capacity(ReplicaReplayPool pool) => pool switch
    {
        ReplicaReplayPool.Critical => CriticalPerVoter,
        ReplicaReplayPool.Forward => ForwardPerVoter,
        ReplicaReplayPool.ReadBarrier => ReadBarrierPerVoter,
        ReplicaReplayPool.DataAppend => DataAppendPerVoter,
        _ => throw new ArgumentOutOfRangeException(nameof(pool))
    };
}

internal enum ReplicaReplayPool { Critical, Forward, ReadBarrier, DataAppend }
