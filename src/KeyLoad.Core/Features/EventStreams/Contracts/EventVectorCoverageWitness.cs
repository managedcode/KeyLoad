namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoverageWitness.SerializerAlias)]
internal sealed record EventVectorCoverageWitness
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-witness.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public ReadOnlyMemory<byte> OriginalSemanticDigest { get; init; }

    [Orleans.Id(2)]
    public EventVectorCoveragePacket[] OriginalPackets { get; init; } = [];

    [Orleans.Id(3)]
    public EventVectorCoveragePacket[] FreshPackets { get; init; } = [];

    [Orleans.Id(4)]
    public ReadOnlyMemory<byte> OriginalSemanticProjectionBytes { get; init; }

}
