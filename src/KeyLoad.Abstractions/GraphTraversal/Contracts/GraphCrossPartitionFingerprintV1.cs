namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionInternalAliases.Fingerprint)]
internal sealed record GraphCrossPartitionFingerprintV1(
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.Version)] int Version,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.SourcePartition)] PartitionRef SourcePartition,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.Graph)] string Graph,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.EdgeId)] string EdgeId,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.Destination)] EntityRef Destination,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.Revision)] long Revision,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.Deleted)] bool Deleted,
    [property: Orleans.Id(GraphCrossPartitionFingerprintFields.Edge)] EdgeRecord Edge);
