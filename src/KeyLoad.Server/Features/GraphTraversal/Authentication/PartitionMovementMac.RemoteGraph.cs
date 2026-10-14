using System.Text;
using KeyLoad.Server.Features.GraphTraversal;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RemoteGraphNativeAuthentication
{
    private static readonly byte[] RemoteGraphRequestDomain = Encoding.ASCII.GetBytes(RemoteGraphNativeProtocol.RequestPurpose);
    private static readonly byte[] RemoteGraphReplyDomain = Encoding.ASCII.GetBytes(RemoteGraphNativeProtocol.ReplyPurpose);

    internal static string SignRemoteGraphRequest(this PartitionMovementMac mac, RemoteGraphNativeRequestV1 request)
        => Convert.ToHexStringLower(mac.DigestNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }),
            RemoteGraphRequestDomain));

    internal static bool VerifyRemoteGraphRequest(this PartitionMovementMac mac, RemoteGraphNativeRequestV1 request)
        => mac.VerifyNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }),
            request.Signature, RemoteGraphRequestDomain);

    internal static string SignRemoteGraphReply(this PartitionMovementMac mac, RemoteGraphNativeReplyV1 reply)
        => Convert.ToHexStringLower(mac.DigestNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }),
            RemoteGraphReplyDomain));

    internal static bool VerifyRemoteGraphReply(this PartitionMovementMac mac, RemoteGraphNativeReplyV1 reply)
        => mac.VerifyNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }),
            reply.Signature, RemoteGraphReplyDomain);
}
