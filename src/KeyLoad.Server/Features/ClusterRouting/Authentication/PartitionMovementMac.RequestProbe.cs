using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementMac
{
    private static readonly byte[] ProbeRequestDomain = Encoding.ASCII.GetBytes(RequestCqrsProbeNativeProtocol.RequestPurpose);
    private static readonly byte[] ProbeReplyDomain = Encoding.ASCII.GetBytes(RequestCqrsProbeNativeProtocol.ReplyPurpose);

    internal string SignProbeRequest(RequestCqrsProbeNativeRequest request)
        => Convert.ToHexStringLower(DigestNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }), ProbeRequestDomain));
    internal bool VerifyProbeRequest(RequestCqrsProbeNativeRequest request)
        => VerifyNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }), request.Signature, ProbeRequestDomain);
    internal string SignProbeReply(RequestCqrsProbeNativeReply reply)
        => Convert.ToHexStringLower(DigestNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }), ProbeReplyDomain));
    internal bool VerifyProbeReply(RequestCqrsProbeNativeReply reply)
        => VerifyNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }), reply.Signature, ProbeReplyDomain);
}
