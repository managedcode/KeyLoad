namespace KeyLoad;

internal static class GraphCrossPartitionProtocol
{
    internal const int CurrentVersion = 1;
    internal const string EventualProjection = "eventual-reverse.v1";
}

internal static class GraphCrossPartitionInternalAliases
{
    internal const string Capacity = "keyload.contract.graph-cross-partition-capacity.v1";
    internal const string CapacityDirection = "keyload.contract.graph-cross-partition-capacity-direction.v1";
    internal const string Fingerprint = "keyload.contract.graph-cross-partition-fingerprint.v1";
}

internal static class GraphCrossPartitionCapacityFields
{
    internal const int Version = 0;
    internal const int Direction = 1;
    internal const int RecordCount = 2;
    internal const int EncodedBytes = 3;
}

internal static class GraphCrossPartitionFingerprintFields
{
    internal const int Version = 0;
    internal const int SourcePartition = 1;
    internal const int Graph = 2;
    internal const int EdgeId = 3;
    internal const int Destination = 4;
    internal const int Revision = 5;
    internal const int Deleted = 6;
    internal const int Edge = 7;
}

[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionInternalAliases.CapacityDirection)]
internal enum GraphCrossPartitionCapacityDirection
{
    [Orleans.Id(0)] PendingIntents = 0,
    [Orleans.Id(1)] ReceiverStates = 1
}

[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionInternalAliases.Capacity)]
internal sealed record GraphCrossPartitionCapacityV1(
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.Version)] int Version,
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.Direction)] GraphCrossPartitionCapacityDirection Direction,
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.RecordCount)] long RecordCount,
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.EncodedBytes)] long EncodedBytes);

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
