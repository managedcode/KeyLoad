namespace KeyLoad;

/// <summary>Stores one coalesced desired reverse-projection state per edge destination.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionAliases.DeliveryIntent)]
internal sealed record GraphCrossPartitionDeliveryIntentV1(
    [property: Orleans.Id(GraphCrossPartitionFields.IntentVersion)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentSourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentGraph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentEdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentDestination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentRevision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentDeleted)] bool Deleted,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentEdge)] EdgeRecord Edge,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentPrincipalId)] string OriginalPrincipalId,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentPolicyEpoch)] long OriginalPolicyEpoch,
    [property: Orleans.Id(GraphCrossPartitionFields.IntentFingerprint)] string Fingerprint);
