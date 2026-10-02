using System.Collections.Immutable;

namespace KeyLoad.Replication;

/// <summary>Defines one immutable fixed-voter replica group and its physical host limits.</summary>
/// <param name="LocalId">Configured identity of the voter owning this host.</param>
/// <param name="VoterIds">Distinct ordered voter identities shared by every host.</param>
/// <param name="Directory">Node-owned replica metadata and snapshot directory.</param>
/// <param name="Incarnation">Shared authority incarnation fencing unrelated replica groups.</param>
public sealed record ReplicaConfiguration(string LocalId, ImmutableArray<string> VoterIds, string Directory, Guid Incarnation)
{
    /// <summary>Gets the number of voters required for durable acknowledgement.</summary>
    public int Majority => VoterIds.Length / 2 + 1;
    /// <summary>Gets the committed-entry interval triggering canonical checkpoints.</summary>
    public int SnapshotThreshold { get; init; } = 1_024;
    /// <summary>Gets the shortest randomized election timeout.</summary>
    public TimeSpan LowerElectionTimeout { get; init; } = TimeSpan.FromSeconds(4);
    /// <summary>Gets the exclusive upper randomized election timeout.</summary>
    public TimeSpan UpperElectionTimeout { get; init; } = TimeSpan.FromSeconds(8);
    /// <summary>Gets the leader heartbeat interval.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Gets the per-peer native RPC deadline.</summary>
    public TimeSpan RpcTimeout { get; init; } = TimeSpan.FromSeconds(2);
    /// <summary>Gets the maximum number of entries in one append batch.</summary>
    public int MaxAppendEntries { get; init; } = 64;
    /// <summary>Gets the maximum encoded append batch size.</summary>
    public int MaxAppendBytes { get; init; } = 16_777_216;
    /// <summary>Gets the maximum bytes acknowledged in one snapshot chunk.</summary>
    public int SnapshotChunkBytes { get; init; } = 262_144;
    /// <summary>Gets the maximum verified canonical snapshot size.</summary>
    public long MaxSnapshotBytes { get; init; } = 4_294_967_296;

    /// <summary>Rejects invalid voter sets, authority identities, timing and byte limits.</summary>
    /// <exception cref="InvalidOperationException">The topology or configured limits are invalid.</exception>
    public void Validate()
    {
        if (Incarnation == Guid.Empty || VoterIds.IsDefault || VoterIds.Length < 3 || VoterIds.Length % 2 == 0
            || VoterIds.Distinct(StringComparer.Ordinal).Count() != VoterIds.Length
            || VoterIds.Any(string.IsNullOrWhiteSpace) || !VoterIds.Contains(LocalId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(ReplicaProtocol.InvalidTopology);
        }
        if (HeartbeatInterval <= TimeSpan.Zero || RpcTimeout <= HeartbeatInterval || LowerElectionTimeout <= RpcTimeout
            || UpperElectionTimeout <= LowerElectionTimeout || SnapshotThreshold < 2 || MaxAppendEntries < 1
            || MaxAppendBytes < 1 || SnapshotChunkBytes is < 1 or > ReplicaProtocol.MaximumChunkBytes || MaxSnapshotBytes < SnapshotChunkBytes)
        {
            throw new InvalidOperationException(ReplicaProtocol.InvalidLimits);
        }
    }
}
