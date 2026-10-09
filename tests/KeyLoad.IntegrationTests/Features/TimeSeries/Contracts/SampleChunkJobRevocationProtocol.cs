namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkJobRevocationProtocol
{
    internal const string PrincipalPrefix = "c1-probe-chunk-creator-";
    internal const string KeyPrefix = "chunk-key-";
    internal const string IdentityAlias = "keyload.core.sample-chunk-work-hint.v1";
    internal const string Database = "database";
    internal const string Domain = "series";
    internal const string TenantPrefix = "chunk-tenant-";
    internal const string Separator = ".";
    internal const string GuidFormat = "N";
    internal const int SecretBytes = 32;
    internal const int GuidBytes = 16;
    internal const int FirstIndex = 0;
    internal const long NextPolicy = 1;
    internal const string Missing = "The chunk creator flow did not retain its original owner.";
}
