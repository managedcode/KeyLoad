namespace KeyLoad.Core.Features.TimeSeries;

[Orleans.GenerateSerializer, Orleans.Alias(SampleChunkManifestEnvelope.SerializerAlias)]
internal sealed record SampleChunkManifestEnvelope([property: Orleans.Id(0)] ReadOnlyMemory<byte> Encoded,
    [property: Orleans.Id(1)] ReadOnlyMemory<byte> Digest)
{
    internal const string SerializerAlias = "keyload.core.sample-chunk-manifest-envelope.v1";
}
