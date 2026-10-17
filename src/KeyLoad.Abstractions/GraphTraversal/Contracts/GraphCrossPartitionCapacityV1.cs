namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionInternalAliases.Capacity)]
internal sealed record GraphCrossPartitionCapacityV1(
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.Version)] int Version,
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.Direction)] GraphCrossPartitionCapacityDirection Direction,
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.RecordCount)] long RecordCount,
    [property: Orleans.Id(GraphCrossPartitionCapacityFields.EncodedBytes)] long EncodedBytes);
