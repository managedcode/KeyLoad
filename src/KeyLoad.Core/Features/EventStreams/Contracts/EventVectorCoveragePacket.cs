namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoveragePacket.SerializerAlias)]
internal sealed record EventVectorCoveragePacket
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-packet.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public ReadOnlyMemory<byte> RequestBytes { get; init; }

    [Orleans.Id(2)]
    public ReadOnlyMemory<byte> RequestSignature { get; init; }

    [Orleans.Id(3)]
    public ReadOnlyMemory<byte> ReplyBytes { get; init; }

    [Orleans.Id(4)]
    public ReadOnlyMemory<byte> ReplySignature { get; init; }

    [Orleans.Id(5)]
    public ReadOnlyMemory<byte> OriginalCaptureBytes { get; init; }

}
