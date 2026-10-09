using System.Collections.Immutable;

namespace KeyLoad.Replication;

/// <summary>Defines one immutable fixed-voter replica group and its physical host limits.</summary>
/// <param name="LocalId">Configured identity of the voter owning this host.</param>
/// <param name="VoterIds">Distinct ordered voter identities shared by every host.</param>
/// <param name="Directory">Node-owned replica metadata and snapshot directory.</param>
/// <param name="Incarnation">Shared authority incarnation fencing unrelated replica groups.</param>
[ConfigurationOptions]
public sealed record ReplicaConfiguration(string LocalId, ImmutableArray<string> VoterIds, string Directory, Guid Incarnation)
{
    private const int MinimumBenchmarkVoters = 1;
    private const int MaximumBenchmarkVoters = 3;
    private const int MinimumProductionVoters = 3;
    private const int DefaultLowerElectionTimeoutSeconds = 4;
    private const int DefaultUpperElectionTimeoutSeconds = 8;
    private const int DefaultHeartbeatIntervalMilliseconds = 250;
    private const int DefaultRpcTimeoutSeconds = 2;
    private const int MajorityDivisor = 2;
    private const int MajorityIncrement = 1;
    private const int EvenVoterRemainder = 0;
    private const int DefaultSnapshotThreshold = 1_024;
    private const int MinimumSnapshotThreshold = 2;
    private const int MinimumTransferBudget = 1;
    private const int DefaultMaxAppendEntries = 64;
    private const int DefaultMaxAppendBytes = 16_777_216;
    private const int DefaultSnapshotChunkBytes = 262_144;
    private const long DefaultMaxSnapshotBytes = 4_294_967_296;
    /// <summary>Permits only explicit trusted benchmark fixed groups of one, two or three voters.</summary>
    public bool BenchmarkTopology { get; init; }
    /// <summary>Gets the number of voters required for durable acknowledgement.</summary>
    public int Majority => VoterIds.Length / MajorityDivisor + MajorityIncrement;
    /// <summary>Gets the committed-entry interval triggering canonical checkpoints.</summary>
    public int SnapshotThreshold { get; init; } = DefaultSnapshotThreshold;
    /// <summary>Gets the shortest randomized election timeout.</summary>
    public TimeSpan LowerElectionTimeout { get; init; } = TimeSpan.FromSeconds(DefaultLowerElectionTimeoutSeconds);
    /// <summary>Gets the exclusive upper randomized election timeout.</summary>
    public TimeSpan UpperElectionTimeout { get; init; } = TimeSpan.FromSeconds(DefaultUpperElectionTimeoutSeconds);
    /// <summary>Gets the leader heartbeat interval.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMilliseconds(DefaultHeartbeatIntervalMilliseconds);
    /// <summary>Gets the per-peer native RPC deadline.</summary>
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(DefaultRpcTimeoutSeconds);
    /// <summary>Gets the maximum number of entries in one append batch.</summary>
    public int MaxAppendEntries { get; init; } = DefaultMaxAppendEntries;
    /// <summary>Gets the maximum encoded append batch size.</summary>
    public int MaxAppendBytes { get; init; } = DefaultMaxAppendBytes;
    /// <summary>Gets the maximum bytes acknowledged in one snapshot chunk.</summary>
    public int SnapshotChunkBytes { get; init; } = DefaultSnapshotChunkBytes;
    /// <summary>Gets the maximum verified canonical snapshot size.</summary>
    public long MaxSnapshotBytes { get; init; } = DefaultMaxSnapshotBytes;

    /// <summary>Rejects invalid voter sets, authority identities, timing and byte limits.</summary>
    /// <exception cref="InvalidOperationException">The topology or configured limits are invalid.</exception>
    public void Validate()
    {
        if (Incarnation == Guid.Empty || VoterIds.IsDefault
            || (BenchmarkTopology ? VoterIds.Length is not (MinimumBenchmarkVoters or MaximumBenchmarkVoters)
                : VoterIds.Length < MinimumProductionVoters || VoterIds.Length % MajorityDivisor == EvenVoterRemainder)
            || VoterIds.Distinct(StringComparer.Ordinal).Count() != VoterIds.Length
            || VoterIds.Any(string.IsNullOrWhiteSpace) || !VoterIds.Contains(LocalId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(ReplicaProtocol.InvalidTopology);
        }
        if (HeartbeatInterval <= TimeSpan.Zero || RpcTimeout <= HeartbeatInterval || LowerElectionTimeout <= RpcTimeout
            || UpperElectionTimeout <= LowerElectionTimeout || SnapshotThreshold < MinimumSnapshotThreshold || MaxAppendEntries < MinimumTransferBudget
            || MaxAppendBytes < MinimumTransferBudget || SnapshotChunkBytes is < MinimumTransferBudget or > ReplicaProtocol.MaximumChunkBytes || MaxSnapshotBytes < SnapshotChunkBytes)
        {
            throw new InvalidOperationException(ReplicaProtocol.InvalidLimits);
        }
    }
}
