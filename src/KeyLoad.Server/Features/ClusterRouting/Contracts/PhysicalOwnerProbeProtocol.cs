namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalOwnerProbeProtocol
{
    internal const int Version = 1;
    internal const int MaximumBodyBytes = 16_384;
    internal const int SecretBytes = 32;
    internal const int SignatureCharacters = 64;
    internal const string Path = "/internal/physical-owners/admission/v1";
    internal const string ContentType = "application/octet-stream";
    internal const string SignatureHeader = "X-KeyLoad-Owner-Proof";
    internal const string CallAlias = "keyload.internal.physical-owner-probe.call.v1";
    internal const string ReplyAlias = "keyload.internal.physical-owner-probe.reply.v1";
    internal const string RequestDomain = "KeyLoad.PhysicalOwnerAdmission.Request.v1\0";
    internal const string ReplyDomain = "KeyLoad.PhysicalOwnerAdmission.Reply.v1\0";
    internal const string InvalidProof = "The physical owner admission proof is invalid.";
    internal const string Unavailable = "The configured physical owner is unavailable.";
}
