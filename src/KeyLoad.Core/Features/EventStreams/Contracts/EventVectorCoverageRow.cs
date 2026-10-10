namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoverageRow.SerializerAlias)]
internal sealed record EventVectorCoverageRow
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-row.v1";

    [Orleans.Id(0)]
    public ReadOnlyMemory<byte> Key { get; init; }

    [Orleans.Id(1)]
    public ReadOnlyMemory<byte> Value { get; init; }

}
