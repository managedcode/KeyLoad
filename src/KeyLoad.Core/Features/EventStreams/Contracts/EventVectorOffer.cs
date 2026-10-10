namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorOffer.SerializerAlias)]
internal sealed record EventVectorOffer
{
    internal const string SerializerAlias = "keyload.core.event-vector-offer.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public long MapRevision { get; init; }

    [Orleans.Id(3)]
    public ReadOnlyMemory<byte> PageDigest { get; init; }

    [Orleans.Id(4)]
    public Guid OriginalReadCommandId { get; init; }

    [Orleans.Id(5)]
    public ReadOnlyMemory<byte> Entries { get; init; }

    [Orleans.Id(6)]
    public DateTimeOffset IssuedAt { get; init; }

    [Orleans.Id(7)]
    public DateTimeOffset ExpiresAt { get; init; }
}
