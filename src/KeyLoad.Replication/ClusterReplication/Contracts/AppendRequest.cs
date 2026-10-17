using System.Collections.Immutable;

namespace KeyLoad.Replication;

/// <summary>Replicates a bounded contiguous suffix after matching previous log evidence.</summary>
/// <param name="LeaderId">Configured voter claiming leadership.</param>
/// <param name="Term">Leader's current term.</param>
/// <param name="PreviousIndex">Position immediately before the proposed suffix.</param>
/// <param name="PreviousTerm">Term required at the previous position.</param>
/// <param name="CommittedIndex">Leader's current committed prefix.</param>
/// <param name="Entries">Ordered entries, or an initialized empty heartbeat batch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.AppendRequest)]
public sealed record AppendRequest(
    [property: Orleans.Id(0)] string LeaderId,
    [property: Orleans.Id(1)] long Term,
    [property: Orleans.Id(2)] long PreviousIndex,
    [property: Orleans.Id(3)] long PreviousTerm,
    [property: Orleans.Id(4)] long CommittedIndex,
    [property: Orleans.Id(5)] ImmutableArray<ReplicaEntry> Entries);
