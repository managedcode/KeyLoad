namespace KeyLoad;

/// <summary>A committed sequence belongs to exactly one atomic partition, or to shared metadata.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(StoragePartitionPosition.SerializerAlias)]
public sealed record StoragePartitionPosition(
    [property: Orleans.Id(0)] PartitionRef? Partition,
    [property: Orleans.Id(1)] long Position)
{
    internal const string SerializerAlias = "keyload.storage-partition-position";
}
