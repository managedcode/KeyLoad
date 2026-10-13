using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OwnerCapacityMac
{
    private const string RequestDomain = "keyload-owner-capacity-request-v1";
    private const string ReplyDomain = "keyload-owner-capacity-reply-v1";
    private static readonly byte[] CapacityRequestDomain =
        Encoding.ASCII.GetBytes(RequestDomain);
    private static readonly byte[] CapacityReplyDomain =
        Encoding.ASCII.GetBytes(ReplyDomain);

    internal static string SignCapacityRequest(PartitionMovementMac mac, OwnerCapacityNativeRequest request)
        => Convert.ToHexStringLower(mac.DigestNative(
            NativeSerialization.Serialize(request with { Signature = string.Empty }), CapacityRequestDomain));

    internal static bool VerifyCapacityRequest(PartitionMovementMac mac, OwnerCapacityNativeRequest request)
        => mac.VerifyNative(NativeSerialization.Serialize(request with { Signature = string.Empty }),
            request.Signature, CapacityRequestDomain);

    internal static string SignCapacityReply(PartitionMovementMac mac, OwnerCapacityNativeReply reply)
        => Convert.ToHexStringLower(mac.DigestNative(
            NativeSerialization.Serialize(reply with { Signature = string.Empty }), CapacityReplyDomain));

    internal static bool VerifyCapacityReply(PartitionMovementMac mac, OwnerCapacityNativeReply reply)
        => mac.VerifyNative(NativeSerialization.Serialize(reply with { Signature = string.Empty }),
            reply.Signature, CapacityReplyDomain);
}
