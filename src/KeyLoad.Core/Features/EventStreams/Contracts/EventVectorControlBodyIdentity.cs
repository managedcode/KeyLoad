namespace KeyLoad.Core;

[Orleans.GenerateSerializer]
[Orleans.Alias(EventVectorControlBodyIdentity.SerializerAlias)]
internal sealed record EventVectorControlBodyIdentity(
    [property: Orleans.Id(EventVectorControlBodyIdentity.VersionField)] int Version,
    [property: Orleans.Id(EventVectorControlBodyIdentity.RequestField)] EventFeedControlRequest OriginalRequest,
    [property: Orleans.Id(EventVectorControlBodyIdentity.EntriesField)] ReadOnlyMemory<byte> OriginalEncodedEntries,
    [property: Orleans.Id(EventVectorControlBodyIdentity.CoverageField)] ReadOnlyMemory<byte> OriginalCoverageSemanticDigest,
    [property: Orleans.Id(EventVectorControlBodyIdentity.SourceBodyField)] ReadOnlyMemory<byte> OriginalSourceNativeBody,
    [property: Orleans.Id(EventVectorControlBodyIdentity.PositionField)] long? OriginalSourcePosition,
    [property: Orleans.Id(EventVectorControlBodyIdentity.PolicyField)] long? OriginalControlPolicyEpoch)
{
    internal const int CleanupFrontierField = 8;
    [Orleans.Id(CleanupFrontierField)]
    public ReadOnlyMemory<byte> OriginalCleanupFrontierSemanticDigest { get; init; }

    internal const int ParentExpiryField = 7;
    [Orleans.Id(ParentExpiryField)]
    public DateTimeOffset OriginalParentExpiresAt { get; init; }

    internal const string SerializerAlias = "keyload.core.event-vector-control-body-identity.v1";
    internal const int VersionField = 0;
    internal const int RequestField = 1;
    internal const int EntriesField = 2;
    internal const int CoverageField = 3;
    internal const int SourceBodyField = 4;
    internal const int PositionField = 5;
    internal const int PolicyField = 6;
}
