namespace KeyLoad.Orleans;

internal static class ReplicaMembershipAuthorityProtocol
{
    internal const string Path = "/internal/orleans/membership/v1";
    internal const string ContentType = "application/octet-stream";
    internal const string RequestPurpose = "keyload-orleans-membership-authority-request-v1";
    internal const string ReplyPurpose = "keyload-orleans-membership-authority-reply-v1";
    internal const string ClusterHeader = "X-KeyLoad-Membership-Cluster";
    internal const string AuthorityPhysicalHeader = "X-KeyLoad-Membership-Authority-Physical";
    internal const string AuthorityIncarnationHeader = "X-KeyLoad-Membership-Authority-Incarnation";
    internal const string CallerPhysicalHeader = "X-KeyLoad-Membership-Caller-Physical";
    internal const string CallerIncarnationHeader = "X-KeyLoad-Membership-Caller-Incarnation";
    internal const string CallerVoterHeader = "X-KeyLoad-Membership-Caller-Voter";
    internal const string CallerSiloHeader = "X-KeyLoad-Membership-Caller-Silo";
    internal const string TimestampHeader = "X-KeyLoad-Membership-TimestampTicks";
    internal const string NonceHeader = "X-KeyLoad-Membership-Nonce";
    internal const string SignatureHeader = "X-KeyLoad-Membership-Signature";
    internal static TimeSpan RequestTimeout { get; } = ReplicaMembershipProtocol.RequestTimeout;
    internal static TimeSpan StartupTimeout { get; } = ReplicaMembershipProtocol.StartupTimeout;
    internal const int Version = 1;
    internal const int MaximumRequestBytes = 65_536;
    internal const int MaximumReplyBytes = 262_144;
    internal const int MaximumSnapshotBytes = 262_144;
    internal const int MaximumRowBytes = 4_096;
    internal const int MaximumRows = 48;
    internal const int MaximumSuspects = 6;
    internal const int MaximumAddressBytes = 256;
    internal const int MaximumHeaderBytes = 4_096;
    internal const int MaximumHeaderValueBytes = 512;
    internal const int MaximumAdmissions = 8;
    internal const int MaximumNonces = 16_384;
    internal const int NonceBytes = 16;
    internal const int NonceCharacters = 22;
}
