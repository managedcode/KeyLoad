namespace KeyLoad.Replication;

/// <summary>Returns durable transfer progress or the verified installed cut.</summary>
/// <param name="Term">Receiver's current term.</param>
/// <param name="Offset">Durably acknowledged image byte prefix.</param>
/// <param name="Installed">Whether the complete image has been installed.</param>
/// <param name="Index">Installed replica position, or zero during upload.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ReplicaSerializationAliases.SnapshotReply)]
public sealed record SnapshotReply(
    [property: Orleans.Id(0)] long Term,
    [property: Orleans.Id(1)] long Offset,
    [property: Orleans.Id(2)] bool Installed,
    [property: Orleans.Id(3)] long Index);
