namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorCoverageSemanticIdentity.SerializerAlias)]
internal sealed record EventVectorCoverageSemanticIdentity
{
    internal const string SerializerAlias = "keyload.core.event-vector-coverage-semantic-identity.v1";

    [Orleans.Id(0)]
    public int Version { get; init; }

    [Orleans.Id(1)]
    public ReadOnlyMemory<byte> OriginalEncodedEntries { get; init; }

    [Orleans.Id(2)]
    public ReadOnlyMemory<byte>[] OriginalCaptureBytes { get; init; } = [];

}
