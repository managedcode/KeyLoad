namespace KeyLoad;

/// <summary>Stores the target partition's revisioned derived reverse-edge high-water state.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.ReceiverState)]
internal sealed record GraphCrossPartitionReceiverState(
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverRevision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverDeleted)] bool Deleted,
    [property: Orleans.Id(GraphCrossPartitionFields.ReceiverFingerprint)] string Fingerprint);
