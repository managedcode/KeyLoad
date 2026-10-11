namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorSourceCapacity.SerializerAlias)]
internal sealed record EventVectorSourceCapacity
{
    internal const string SerializerAlias = "keyload.core.event-vector-source-capacity.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public PartitionRef SourcePartition { get; init; } = null!;

    [Orleans.Id(2)]
    public long OpenPins { get; init; }

    [Orleans.Id(3)]
    public long RetainedBytes { get; init; }
}
