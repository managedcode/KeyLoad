namespace KeyLoad.Replication;

internal static class PeerDiscoveryProtocol
{
    internal const string Purpose = "keyload-discovery-request-data-epoch7";
    internal const string TimeHeader = "X-KeyLoad-Time";
    internal const string NonceHeader = "X-KeyLoad-Nonce";
    internal const string SignatureHeader = "X-KeyLoad-Signature";
    internal const string NonceFormat = "N";
    internal const string Separator = "\n";
    internal const string InvalidRequest = "Only a bodyless GET of the exact peer discovery path is permitted.";
    internal const string InvalidConfiguration = "Peer discovery requires a 32-byte secret, a clock and positive timeout/replay limits.";
    internal const string ReplayCapacityExceeded = "Authenticated peer discovery replay admission is exhausted.";
    internal const string Unavailable = "Authenticated peer discovery transport is unavailable.";
    internal const int SecretBytes = 32;
    internal const int HashCharacters = SecretBytes * 2;
    internal const int NonceCharacters = 32;
    internal const int MaximumTimestampCharacters = 20;
    internal const int MaximumAuthorityCharacters = 2048;
    internal const int BodyProbeBytes = 1;
}
