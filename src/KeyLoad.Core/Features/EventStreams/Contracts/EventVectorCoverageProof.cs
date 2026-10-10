namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoverageProof.SerializerAlias)]
internal sealed record EventVectorCoverageProof
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-proof.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public PartitionRef ControlPartition { get; init; } = null!;

    [Orleans.Id(3)]
    public long CoverageGeneration { get; init; }

    [Orleans.Id(4)]
    public ReadOnlyMemory<byte> OriginalSemanticDigest { get; init; }

    [Orleans.Id(5)]
    public EventVectorCoveragePacket[] OriginalPackets { get; init; } = [];

    [Orleans.Id(6)]
    public ReadOnlyMemory<byte> OriginalEncodedEntries { get; init; }

    [Orleans.Id(7)]
    public ReadOnlyMemory<byte> EntryChecksum { get; init; }

    [Orleans.Id(8)]
    public ReadOnlyMemory<byte> OriginalSemanticProjectionBytes { get; init; }

}
