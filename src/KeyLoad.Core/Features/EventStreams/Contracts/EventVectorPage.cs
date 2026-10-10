namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorPage.SerializerAlias)]
internal sealed record EventVectorPage
{
    internal const string SerializerAlias = "keyload.core.event-vector-page.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public Guid MapId { get; init; }

    [Orleans.Id(2)]
    public int PageOrdinal { get; init; }

    [Orleans.Id(3)]
    public ReadOnlyMemory<byte> Entries { get; init; }

    [Orleans.Id(4)]
    public ReadOnlyMemory<byte> Checksum { get; init; }
}
