namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCapacity.SerializerAlias)]
internal sealed record EventVectorCapacity
{
    internal const string SerializerAlias = "keyload.core.event-vector-capacity.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(2)]
    public long OpenMaps { get; init; }

    [Orleans.Id(3)]
    public long RetainedBytes { get; init; }
}
