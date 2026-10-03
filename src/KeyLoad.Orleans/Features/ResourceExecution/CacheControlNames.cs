namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlNames
{
    private const string Prefix = "keyload.cache.control.";
    private const string Suffix = ".v1";
    internal const string Digest = Prefix + "digest" + Suffix;
    internal const string PhysicalBinding = Prefix + "physical-binding" + Suffix;
    internal const string Header = Prefix + "header" + Suffix;
    internal const string ReadyProof = Prefix + "ready-proof" + Suffix;
    internal const string PrepareRequest = Prefix + "prepare-request" + Suffix;
    internal const string GrantRequest = Prefix + "grant-request" + Suffix;
    internal const string RevokeRequest = Prefix + "revoke-request" + Suffix;
    internal const string RefreshRequest = Prefix + "refresh-request" + Suffix;
    internal const string ReplyCorrelation = Prefix + "reply-correlation" + Suffix;
    internal const string PrepareReply = Prefix + "prepare-reply" + Suffix;
    internal const string GrantReply = Prefix + "grant-reply" + Suffix;
    internal const string RevokeReply = Prefix + "revoke-reply" + Suffix;
    internal const string RefreshReply = Prefix + "refresh-reply" + Suffix;
    internal const string KeyPurpose = "keyload-cache-key-v1";
    internal const string RequestPurpose = "keyload-cache-control-request-v1";
    internal const string ReplyPurpose = "keyload-cache-control-reply-v1";
    internal const string ProofPurpose = "keyload-cache-ready-proof-v1";
    internal const string CorrelationPurpose = "keyload-cache-request-correlation-v1";
}
