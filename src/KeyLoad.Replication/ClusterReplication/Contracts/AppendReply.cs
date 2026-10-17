namespace KeyLoad.Replication;

/// <summary>Returns an append acknowledgement or bounded conflict-retry position.</summary>
/// <param name="Term">Receiver's latest observed term.</param>
/// <param name="Accepted">Whether the previous evidence and suffix matched.</param>
/// <param name="MatchedIndex">Last durably matching position.</param>
/// <param name="NextIndex">Next position the leader should send.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.AppendReply)]
public sealed record AppendReply(
    [property: Orleans.Id(0)] long Term,
    [property: Orleans.Id(1)] bool Accepted,
    [property: Orleans.Id(2)] long MatchedIndex,
    [property: Orleans.Id(3)] long NextIndex);
