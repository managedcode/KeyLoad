namespace KeyLoad.Replication;

/// <summary>Returns the receiver's current durable election decision.</summary>
/// <param name="Term">Receiver's latest observed term.</param>
/// <param name="Granted">Whether this candidate received the durable vote.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.VoteReply)]
public sealed record VoteReply(
    [property: Orleans.Id(0)] long Term,
    [property: Orleans.Id(1)] bool Granted);
